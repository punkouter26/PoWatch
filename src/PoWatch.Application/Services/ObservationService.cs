using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Models;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Application.Services;

public sealed class ObservationService(
    IObservationRepository observationRepository,
    ISubjectRepository subjectRepository,
    IObservationProcessingGate processingGate,
    IOptions<FeatureFlagsOptions> featureFlags,
    IOptions<ObserverOptions> observerOptions,
    ILogger<ObservationService> logger)
{
    public async Task<IngestObservationResult> IngestAsync(IngestObservationRequest request, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "Observation poll start. ObservedAtUtc={ObservedAtUtc} SubjectHint={SubjectHint} Activity={Activity}",
            request.ObservedAtUtc,
            request.SubjectHint,
            request.Activity);

        if (!featureFlags.Value.ObservationLoopEnabled)
        {
            logger.LogInformation("Observation ingest ignored because observation loop is disabled by feature flag.");
            return new IngestObservationResult
            {
                Accepted = false,
                Dropped = true,
                Detail = "Observation loop is disabled by feature flag."
            };
        }

        if (!processingGate.TryEnter())
        {
            logger.LogDebug(
                "Poll dropped to avoid backlog. ObservedAtUtc={ObservedAtUtc}, SubjectHint={SubjectHint}, Activity={Activity}",
                request.ObservedAtUtc,
                request.SubjectHint,
                request.Activity);

            return new IngestObservationResult
            {
                Accepted = false,
                Dropped = true,
                Detail = "Frame dropped because previous inference is still running."
            };
        }

        try
        {
            var subject = await subjectRepository.GetOrCreateAsync(request.SubjectHint, cancellationToken);
            var isOutlier = !ClinicalTagParser.TryExtract(request.ClinicalPayload, out var extracted);
            var description = isOutlier ? "Clinical outlier: malformed inference payload." : extracted;

            if (isOutlier)
            {
                logger.LogWarning(
                    "Clinical outlier captured. SubjectId={SubjectId}, Payload={Payload}",
                    subject.SubjectId,
                    request.ClinicalPayload);
            }

            var latest = await observationRepository.GetLatestForSubjectAsync(subject.SubjectId, cancellationToken);
            if (latest is not null &&
                string.Equals(latest.Activity, request.Activity, StringComparison.OrdinalIgnoreCase) &&
                !latest.IsClinicalOutlier &&
                !isOutlier)
            {
                logger.LogInformation(
                    "Redundant observation skipped. SubjectId={SubjectId} Activity={Activity} LastObservedAtUtc={LastObservedAtUtc}",
                    subject.SubjectId,
                    request.Activity,
                    latest.ObservedAtUtc);

                return new IngestObservationResult
                {
                    Accepted = true,
                    Dropped = false,
                    SkippedAsRedundant = true,
                    SubjectId = subject.SubjectId,
                    SubjectDisplayName = subject.DisplayName,
                    Detail = "No state change detected; redundant observation skipped."
                };
            }

            var observation = new ObservationEvent
            {
                ObservedAtUtc = request.ObservedAtUtc,
                SubjectId = subject.SubjectId,
                SubjectDisplayName = subject.DisplayName,
                Activity = request.Activity,
                ClinicalDescription = description,
                IsSignificant = request.IsSignificant,
                SignificantReason = request.SignificantReason,
                IsClinicalOutlier = isOutlier,
                ImageReference = request.IsSignificant && featureFlags.Value.SaveSignificantImages
                    ? $"significant-images/{DateOnly.FromDateTime(request.ObservedAtUtc.UtcDateTime):yyyyMMdd}/{subject.SubjectId}/{Guid.NewGuid():N}.svg"
                    : null
            };

            await observationRepository.AddAsync(observation, cancellationToken);

            logger.LogInformation(
                "Observation persisted. EventId={EventId}, SubjectId={SubjectId}, Significant={Significant}, Outlier={Outlier}, ObservedAtUtc={ObservedAtUtc}",
                observation.Id,
                observation.SubjectId,
                observation.IsSignificant,
                observation.IsClinicalOutlier,
                observation.ObservedAtUtc);

            return new IngestObservationResult
            {
                Accepted = true,
                Dropped = false,
                IsOutlier = observation.IsClinicalOutlier,
                EventId = observation.Id.ToString("N"),
                SubjectId = observation.SubjectId,
                SubjectDisplayName = observation.SubjectDisplayName,
                Detail = observation.IsClinicalOutlier ? "Clinical outlier recorded." : "Observation recorded."
            };
        }
        finally
        {
            processingGate.Exit();
        }
    }

    public ObserverRuntimeState GetRuntimeState() => new()
    {
        ObservationLoopEnabled = featureFlags.Value.ObservationLoopEnabled,
        TtsAnnouncementsEnabled = featureFlags.Value.TtsAnnouncementsEnabled,
        SaveSignificantImages = featureFlags.Value.SaveSignificantImages,
        PollIntervalSeconds = observerOptions.Value.PollingIntervalSeconds,
        CapturedAtUtc = DateTimeOffset.UtcNow,
        Status = featureFlags.Value.ObservationLoopEnabled ? "Idle" : "Disabled",
        StatusDetail = featureFlags.Value.ObservationLoopEnabled
            ? "Observer loop ready for the next local inference poll."
            : "Observation loop disabled by operator."
    };
}

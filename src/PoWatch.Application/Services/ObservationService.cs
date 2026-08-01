using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Shared.Models;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;

namespace PoWatch.Application.Services;

public sealed class ObservationService(
    IObservationRepository observationRepository,
    ISubjectRepository subjectRepository,
    IObservationProcessingGate processingGate,
    ITelemetryContentSanitizer telemetryContentSanitizer,
    AlertThresholdEvaluator thresholdEvaluator,
    IOptions<FeatureFlagsOptions> featureFlags,
    IOptions<ObserverOptions> observerOptions,
    ILogger<ObservationService> logger)
{
    public async Task<IngestObservationResultDto> IngestAsync(IngestObservationRequestDto request, CancellationToken cancellationToken)
    {
        logger.PollStart(request.ObservedAtUtc, request.SubjectHint, request.Activity);

        if (!featureFlags.Value.ObservationLoopEnabled)
        {
            logger.IngestIgnoredLoopDisabled();
            return new IngestObservationResultDto
            {
                Accepted = false,
                Dropped = true,
                Detail = "Observation loop is disabled by feature flag."
            };
        }

        if (!processingGate.TryEnter())
        {
            logger.PollDropped(request.ObservedAtUtc, request.SubjectHint, request.Activity);

            return new IngestObservationResultDto
            {
                Accepted = false,
                Dropped = true,
                Detail = "Frame dropped because previous inference is still running."
            };
        }

        try
        {
            if (featureFlags.Value.EnableTelemetrySanitizer)
            {
                if (!telemetryContentSanitizer.TrySanitize(request, out var sanitizedRequest, out var sanitizationReason))
                {
                    logger.RejectedBySanitizer(sanitizationReason, request.Activity, request.SubjectHint);

                    return new IngestObservationResultDto
                    {
                        Accepted = false,
                        Dropped = true,
                        Detail = $"Rejected by telemetry sanitizer: {sanitizationReason}"
                    };
                }

                request = sanitizedRequest;
            }

            var subject = await subjectRepository.GetOrCreateAsync(request.SubjectHint, cancellationToken);
            var isOutlier = !ClinicalTagParser.TryExtract(request.ClinicalPayload, out var extracted);
            var description = isOutlier ? "Clinical outlier: malformed inference payload." : extracted;

            if (isOutlier)
            {
                logger.ClinicalOutlier(subject.SubjectId, request.ClinicalPayload);
            }

            var observedAtUtc = DateTimeOffset.UtcNow;
            var observation = new ObservationEvent
            {
                // Idempotency: a client-supplied key gives the row a stable identity so a retried submit
                // maps to the same Id; the repository treats the resulting 409 as success (no duplicate).
                Id = request.IdempotencyKey is { } idempotencyKey ? ObservationEventId.From(idempotencyKey) : ObservationEventId.New(),
                // Server-authoritative timestamp; client-supplied ObservedAtUtc is not trusted to prevent backdating.
                ObservedAtUtc = observedAtUtc,
                SubjectId = subject.SubjectId,
                SubjectDisplayName = subject.DisplayName,
                Activity = request.Activity,
                ClinicalDescription = description,
                IsSignificant = request.IsSignificant,
                SignificantReason = request.SignificantReason,
                IsClinicalOutlier = isOutlier,
                ImageReference = request.IsSignificant && featureFlags.Value.SaveSignificantImages
                    ? $"significant-images/{DateOnly.FromDateTime(observedAtUtc.UtcDateTime):yyyyMMdd}/{subject.SubjectId}/{Guid.NewGuid():N}.jpg"
                    : null
            };

            // Always persist the observation - do not skip!
            // Previously, stable-state observations were silently dropped. This caused:
            // 1. Drift detection to miss stable subjects
            // 2. Archives to never record them
            // 3. Alert thresholds to never evaluate them
            // 
            // The redundancy flag is now set on the observation itself, allowing downstream
            // consumers to filter if needed, while ensuring ALL events are persisted.
            var isRedundant = IsRedundantObservation(subject, request.Activity, isOutlier);

            await observationRepository.AddAsync(observation, cancellationToken);
            await subjectRepository.UpdateLastActivityAsync(subject.SubjectId, observation.Activity, observation.IsClinicalOutlier, cancellationToken);

            var triggeredAlerts = thresholdEvaluator.Evaluate(observation);

            logger.ObservationPersisted(
                (Guid)observation.Id,
                observation.SubjectId,
                observation.IsSignificant,
                observation.IsClinicalOutlier,
                observation.ImageReference,
                observation.ObservedAtUtc,
                triggeredAlerts.Count);

            return new IngestObservationResultDto
            {
                Accepted = true,
                Dropped = false,
                IsOutlier = observation.IsClinicalOutlier,
                EventId = observation.Id.Value.ToString("N"),
                SubjectId = observation.SubjectId,
                SubjectDisplayName = observation.SubjectDisplayName,
                ImageReference = observation.ImageReference,
                SkippedAsRedundant = isRedundant,
                Detail = isRedundant
                    ? "Observation recorded with stable-state flag."
                    : (observation.IsClinicalOutlier ? "Clinical outlier recorded." : "Observation recorded."),
                TriggeredAlerts = triggeredAlerts
            };
        }
        finally
        {
            processingGate.Exit();
        }
    }

    public ObserverRuntimeStateDto GetRuntimeState() => new()
    {
        ObservationLoopEnabled = featureFlags.Value.ObservationLoopEnabled,
        SaveSignificantImages = featureFlags.Value.SaveSignificantImages,
        DeveloperModeEnabled = featureFlags.Value.DeveloperBypassAuth,
        PollIntervalSeconds = observerOptions.Value.PollingIntervalSeconds,
        CapturedAtUtc = DateTimeOffset.UtcNow,
        Status = !featureFlags.Value.ObservationLoopEnabled
            ? "Disabled"
            : processingGate.IsProcessing
                ? "Active"
                : "Idle",
        StatusDetail = !featureFlags.Value.ObservationLoopEnabled
            ? "Observation loop disabled by operator."
            : processingGate.IsProcessing
                ? "Observer loop is processing an inference cycle."
                : "Observer loop ready for the next local inference poll."
    };

    /// <summary>
    /// Determines if an observation is redundant (stable activity matching cached state).
    /// The observation is STILL persisted, but this flag indicates it represents no change.
    /// </summary>
    private static bool IsRedundantObservation(SubjectProfile subject, string activity, bool isOutlier)
    {
        if (string.IsNullOrWhiteSpace(activity))
            return false;

        // Only mark as redundant if subject has cached state and activity matches
        if (subject.LastActivity is null)
            return false;

        // Don't mark as redundant if this is an outlier (outliers always have significance)
        if (isOutlier || subject.LastActivityIsOutlier)
            return false;

        // Check if activity is "stable" (low-change activities)
        if (!IsStableActivity(activity))
            return false;

        // Check if it matches the cached state
        return string.Equals(subject.LastActivity, activity, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStableActivity(string activity)
    {
        return activity.Contains("desk", StringComparison.OrdinalIgnoreCase)
            || activity.Contains("sit", StringComparison.OrdinalIgnoreCase)
            || activity.Contains("sleep", StringComparison.OrdinalIgnoreCase)
            || activity.Contains("idle", StringComparison.OrdinalIgnoreCase)
            || activity.Contains("rest", StringComparison.OrdinalIgnoreCase);
    }
}

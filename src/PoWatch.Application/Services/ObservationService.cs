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
    IOptions<FeatureFlagsOptions> featureFlags,
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
            var description = ExtractCaption(request.ClinicalPayload) ?? request.Activity;

            // Significance is decided here, not by the caller. The inference worker used to set it from
            // the caption's length, which flagged every well-formed observation; deriving it from the
            // content instead means "Notable" is worth triaging again. A caller that supplies its own
            // reason (the dev-tool injectors, contract tests) still wins — an explicit reason is a
            // deliberate assertion, not a default.
            var callerAssertedSignificance = !string.IsNullOrWhiteSpace(request.SignificantReason);
            var verdict = ActivitySignificanceClassifier.Classify(request.Activity, description);
            var isSignificant = callerAssertedSignificance ? request.IsSignificant : verdict.IsSignificant;
            var significantReason = callerAssertedSignificance ? request.SignificantReason : verdict.Reason;
            // Score + Confidence always come from the classifier, even when the caller asserts the band.
            // A test injector that says "this is Urgent because I said so" still gets a useful
            // strength-of-signal reading for the heatmap gradient.
            var significanceScore = verdict.Score;
            var significanceConfidence = verdict.Confidence;

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
                IsSignificant = isSignificant,
                SignificantReason = significantReason,
                SignificanceScore = significanceScore,
                SignificanceConfidence = significanceConfidence,
                ImageReference = isSignificant && featureFlags.Value.SaveSignificantImages
                    ? $"significant-images/{DateOnly.FromDateTime(observedAtUtc.UtcDateTime):yyyyMMdd}/{subject.SubjectId}/{Guid.NewGuid():N}.jpg"
                    : null
            };

            // Always persist the observation - do not skip!
            // Previously, stable-state observations were silently dropped. This caused:
            // 1. Drift detection to miss stable subjects
            // 2. Archives to never record them
            // 
            // The redundancy flag is now set on the observation itself, allowing downstream
            // consumers to filter if needed, while ensuring ALL events are persisted.
            var isRedundant = IsRedundantObservation(subject, request.Activity);

            await observationRepository.AddAsync(observation, cancellationToken);
            await subjectRepository.UpdateLastActivityAsync(subject.SubjectId, observation.Activity, observation.IsClinicalOutlier, cancellationToken);

            logger.ObservationPersisted(
                (Guid)observation.Id,
                observation.SubjectId,
                observation.IsSignificant,
                observation.IsClinicalOutlier,
                observation.ImageReference,
                observation.ObservedAtUtc);

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
                // Echo the server's verdict so the client stops second-guessing it: the Live Room uses
                // this to decide the alert level, whether to upload an evidence frame, and what to say.
                IsSignificant = observation.IsSignificant,
                SignificantReason = observation.SignificantReason,
                SignificanceScore = observation.SignificanceScore,
                SignificanceConfidence = observation.SignificanceConfidence,
                Detail = isRedundant
                    ? "Observation recorded with stable-state flag."
                    : "Observation recorded."
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
        PollIntervalSeconds = featureFlags.Value.PollingIntervalSeconds,
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
    private static bool IsRedundantObservation(SubjectProfile subject, string activity)
    {
        if (string.IsNullOrWhiteSpace(activity))
            return false;

        // Only mark as redundant if subject has cached state and activity matches
        if (subject.LastActivity is null)
            return false;

        // Check if activity is "stable" (low-change activities)
        if (!IsStableActivity(activity))
            return false;

        // Check if it matches the cached state
        return string.Equals(subject.LastActivity, activity, StringComparison.OrdinalIgnoreCase);
    }

    private const int MaxCaptionLength = 500;

    /// <summary>
    /// Pulls the caption out of the worker's payload. The VLM worker wraps it in &lt;S&gt;…&lt;E&gt; markers;
    /// anything without markers is taken as-is. Returns null when nothing usable remains.
    /// </summary>
    private static string? ExtractCaption(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        var caption = payload.Replace("<S>", string.Empty, StringComparison.Ordinal)
            .Replace("<E>", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (caption.Length == 0)
            return null;

        return caption.Length > MaxCaptionLength ? caption[..MaxCaptionLength] : caption;
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

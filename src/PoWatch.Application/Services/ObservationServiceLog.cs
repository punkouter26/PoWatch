using Microsoft.Extensions.Logging;

namespace PoWatch.Application.Services;

/// <summary>
/// Source-generated, allocation-free log messages for the per-poll observation ingest hot path
/// (rule 6.1: [LoggerMessage] eliminates value boxing and message-template string allocation).
/// </summary>
internal static partial class ObservationServiceLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug,
        Message = "Observation poll start. ObservedAtUtc={ObservedAtUtc} SubjectHint={SubjectHint} Activity={Activity}")]
    public static partial void PollStart(this ILogger logger, DateTimeOffset observedAtUtc, string? subjectHint, string? activity);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Observation ingest ignored because observation loop is disabled by feature flag.")]
    public static partial void IngestIgnoredLoopDisabled(this ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug,
        Message = "Poll dropped to avoid backlog. ObservedAtUtc={ObservedAtUtc} SubjectHint={SubjectHint} Activity={Activity}")]
    public static partial void PollDropped(this ILogger logger, DateTimeOffset observedAtUtc, string? subjectHint, string? activity);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "Observation rejected by telemetry sanitizer. Reason={Reason} Activity={Activity} SubjectHint={SubjectHint}")]
    public static partial void RejectedBySanitizer(this ILogger logger, string? reason, string? activity, string? subjectHint);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning,
        Message = "Clinical outlier captured. SubjectId={SubjectId} Payload={Payload}")]
    public static partial void ClinicalOutlier(this ILogger logger, string subjectId, string? payload);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information,
        Message = "Observation persisted. EventId={EventId} SubjectId={SubjectId} Significant={Significant} Outlier={Outlier} ImageReference={ImageReference} ObservedAtUtc={ObservedAtUtc} TriggeredAlerts={TriggeredAlertCount}")]
    public static partial void ObservationPersisted(this ILogger logger, Guid eventId, string subjectId, bool significant, bool outlier, string? imageReference, DateTimeOffset observedAtUtc, int triggeredAlertCount);
}

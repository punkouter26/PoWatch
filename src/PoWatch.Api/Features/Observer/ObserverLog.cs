using Microsoft.Extensions.Logging;

namespace PoWatch.Api.Features.Observer;

/// <summary>
/// Source-generated log messages for the observer ingest endpoint. Replaces the previous plain
/// <c>ILogger.LogInformation(...)</c> calls that eagerly allocated <c>Activity.Current?.TraceId.ToString()</c>
/// every ingest cycle regardless of sink level. The trace id is already carried on the Activity /
/// Serilog correlation context, so it is dropped from the message arguments.
/// </summary>
internal static partial class ObserverLog
{
    [LoggerMessage(EventId = 3100, Level = LogLevel.Information,
        Message = "Observer ingest received. Activity={Activity} SubjectHint={SubjectHint}")]
    public static partial void IngestReceived(ILogger logger, string activity, string? subjectHint);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information,
        Message = "Observer ingest completed. Accepted={Accepted} Dropped={Dropped} SubjectId={SubjectId}")]
    public static partial void IngestCompleted(ILogger logger, bool accepted, bool dropped, string subjectId);
}

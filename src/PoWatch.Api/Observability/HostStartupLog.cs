using System.Diagnostics;

namespace PoWatch.Api.Observability;

/// <summary>
/// Zero-allocation startup milestones. Each milestone emits a single
/// structured Serilog event with a stable EventId so Application Insights
/// timelines collapse cleanly during ANCM / Kestrel cold-start analysis.
///
/// Why direct Serilog instead of <c>[LoggerMessage]</c>: the bootstrap runs
/// BEFORE the host's <c>Microsoft.Extensions.Logging.ILoggerFactory</c> is
/// built, so we cannot pass an MEL <c>ILogger</c> through a generated method.
/// Serilog's static <c>Log.Logger</c> is already configured at the very top
/// of <c>Program.cs</c>, so we route everything through it.
/// </summary>
public static class HostStartupLog
{
    private static readonly ActivitySource PoWatchStartupActivitySource = new("PoWatch.Startup");

    // Last milestone reached, captured so the /diag/boot readiness endpoint can report exactly how far a
    // cold start progressed before a failure. Single writer during host construction; plain volatile reads
    // are sufficient (no lock needed for a diagnostic snapshot).
    private static volatile string _lastStage = nameof(Stage.BuilderCreated);
    private static long _lastStageElapsedMs;

    /// <summary>Name of the most recent startup milestone reached (for readiness diagnostics).</summary>
    public static string LastStage => _lastStage;

    /// <summary>Elapsed milliseconds recorded at the most recent milestone.</summary>
    public static long LastStageElapsedMs => System.Threading.Interlocked.Read(ref _lastStageElapsedMs);

    // EventIds 5001-5010 are reserved for host-startup milestones. They are
    // emitted in order so a dashboard can render a horizontal "boot timeline".
    public enum Stage
    {
        BuilderCreated = 5001,
        ServicesConfigured = 5002,
        AuthWired = 5003,
        KeyVaultLoaded = 5004,
        PipelineBuilt = 5005,
        Listening = 5006,
        FirstRequest = 5007,
        Shutdown = 5008,
        PortConflictResolved = 5009,
        PortConflictFatal = 5010
    }

    /// <summary>
    /// Emit a structured Serilog Information event with the milestone name,
    /// elapsed milliseconds since the last call, and the current OTel trace id
    /// (if any) so log lines correlate with App Insights / OTel spans.
    /// </summary>
    public static void Milestone(Serilog.ILogger logger, Stage stage, Stopwatch sw)
    {
        sw.Stop();
        _lastStage = stage.ToString();
        System.Threading.Interlocked.Exchange(ref _lastStageElapsedMs, sw.ElapsedMilliseconds);
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty;
        logger
            .ForContext("EventId", (int)stage)
            .Information(
                "Host startup: {Stage} in {ElapsedMs}ms (TraceId={TraceId})",
                stage.ToString(),
                sw.ElapsedMilliseconds,
                traceId);
        sw.Restart();
    }

    public static void PortConflictResolved(Serilog.ILogger logger, int requested, int actual, long retryMs)
    {
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty;
        logger
            .ForContext("EventId", (int)Stage.PortConflictResolved)
            .Warning(
                "Port conflict on requested port {RequestedPort}; rebound to {ActualPort} after {RetryMs}ms (TraceId={TraceId})",
                requested,
                actual,
                retryMs,
                traceId);
    }

    public static void PortConflictFatal(Serilog.ILogger logger, string requestedPorts, int attemptCount)
    {
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty;
        logger
            .ForContext("EventId", (int)Stage.PortConflictFatal)
            .Fatal(
                "Unable to bind Kestrel to any port in [{RequestedPorts}] after {AttemptCount} attempts (TraceId={TraceId})",
                requestedPorts,
                attemptCount,
                traceId);
    }
}

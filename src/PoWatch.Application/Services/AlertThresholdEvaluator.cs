using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

/// <summary>
/// Evaluates configured alert threshold rules against a per-subject in-memory rolling event window.
/// Thread-safe singleton: one ConcurrentDictionary entry per subject, each entry prunes its own
/// ring of events under a per-window lock on every write. There is deliberately NO background
/// sweeper — a stale subject window costs one small object, and the previous design walked every
/// key in the dictionary on every ingest (O(subjects) per event, with per-key locks) to reclaim
/// memory that a single-room app never accumulates.
/// </summary>
public sealed class AlertThresholdEvaluator(
    IOptions<AlertThresholdOptions> options,
    ILogger<AlertThresholdEvaluator> logger)
{
    // Key: subjectId — Value: timestamped event entries within the widest configured window.
    private readonly ConcurrentDictionary<string, SubjectWindow> _windows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records the event for <paramref name="observation"/> and evaluates all enabled rules.
    /// Returns any rules that fired on this call.
    /// </summary>
    public IReadOnlyList<ThresholdAlertDto> Evaluate(ObservationEvent observation)
    {
        if (!options.Value.Enabled)
            return [];

        var enabledRules = options.Value.Rules.Where(r => r.Enabled).ToList();
        if (enabledRules.Count == 0)
            return [];

        var now = observation.ObservedAtUtc;
        var maxWindowMinutes = enabledRules.Max(r => r.WindowMinutes);
        var cutoff = now.AddMinutes(-maxWindowMinutes);

        // Get or create subject window atomically; AddEvent prunes entries older than the
        // widest rule window, so no window ever grows without bound.
        var window = _windows.AddOrUpdate(
            observation.SubjectId,
            _ => new SubjectWindow(now, observation),
            (_, existing) => existing.AddEvent(now, observation, cutoff));

        // Amortised safety valve: if a long-running deployment somehow accumulates a large
        // number of one-off subject windows, drop the stale ones on the ingest that noticed
        // — instead of paying a sweep on every single event.
        if (_windows.Count > MaxTrackedSubjects)
            EvictStaleWindows(now.AddMinutes(-maxWindowMinutes));

        // Evaluate rules using the current window state
        return EvaluateRules(window, enabledRules, observation.SubjectId, now);
    }

    /// <summary>Soft cap before the amortised eviction pass runs. Generous on purpose.</summary>
    private const int MaxTrackedSubjects = 128;

    private void EvictStaleWindows(DateTimeOffset staleCutoff)
    {
        foreach (var (key, window) in _windows)
        {
            if (window.IsStale(staleCutoff))
            {
                _windows.TryRemove(key, out _);
                logger.LogDebug(
                    "Evicted stale alert threshold window. SubjectId={SubjectId} Cutoff={Cutoff}",
                    key,
                    staleCutoff);
            }
        }
    }

    private List<ThresholdAlertDto> EvaluateRules(
        SubjectWindow window,
        List<AlertThresholdRule> enabledRules,
        string subjectId,
        DateTimeOffset now)
    {
        var triggered = new List<ThresholdAlertDto>();

        foreach (var rule in enabledRules)
        {
            var ruleCutoff = now.AddMinutes(-rule.WindowMinutes);
            var count = window.GetEventCount(rule.Metric, ruleCutoff);

            if (count >= rule.Threshold)
            {
                logger.LogWarning(
                    "Alert threshold breached. Rule={Rule} SubjectId={SubjectId} Count={Count} Threshold={Threshold} Window={WindowMinutes}min",
                    rule.Name,
                    subjectId,
                    count,
                    rule.Threshold,
                    rule.WindowMinutes);

                triggered.Add(new ThresholdAlertDto
                {
                    RuleName = rule.Name,
                    Description = string.IsNullOrWhiteSpace(rule.Description)
                        ? $"{count} events in {rule.WindowMinutes} minutes."
                        : rule.Description,
                    SubjectId = subjectId,
                    TriggeredAtUtc = now
                });
            }
        }

        return triggered;
    }

    /// <summary>
    /// Internal window class that maintains ordered events and supports pruning.
    /// Thread-safe internal operations with immutable snapshot for reads.
    /// </summary>
    private sealed class SubjectWindow
    {
        private readonly List<RollingEntry> _entries = new();
        private readonly object _lock = new();
        private DateTimeOffset _lastEventTime;

        public SubjectWindow(DateTimeOffset eventTime, ObservationEvent observation)
        {
            _lastEventTime = eventTime;
            _entries.Add(new RollingEntry(eventTime, observation.IsSignificant, observation.IsClinicalOutlier));
        }

        public SubjectWindow AddEvent(DateTimeOffset eventTime, ObservationEvent observation, DateTimeOffset cutoff)
        {
            lock (_lock)
            {
                // Prune old entries
                _entries.RemoveAll(e => e.ObservedAtUtc < cutoff);
                _entries.Add(new RollingEntry(eventTime, observation.IsSignificant, observation.IsClinicalOutlier));
                _lastEventTime = eventTime;
            }
            return this;
        }

        public int GetEventCount(AlertMetric metric, DateTimeOffset cutoff)
        {
            lock (_lock)
            {
                return _entries.Count(e =>
                    e.ObservedAtUtc >= cutoff &&
                    metric switch
                    {
                        AlertMetric.Outlier => e.IsOutlier,
                        AlertMetric.Significant => e.IsSignificant,
                        AlertMetric.Any => true,
                        _ => false
                    });
            }
        }

        public bool IsStale(DateTimeOffset cutoff)
        {
            lock (_lock)
            {
                return _entries.Count == 0 || _lastEventTime < cutoff;
            }
        }
    }

    private sealed record RollingEntry(DateTimeOffset ObservedAtUtc, bool IsSignificant, bool IsOutlier);
}

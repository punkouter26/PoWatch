using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

/// <summary>
/// Evaluates configured alert threshold rules against a per-subject in-memory rolling event window.
/// Thread-safe singleton using pure ConcurrentDictionary operations with Channel-based ingestion.
/// Window entries expire automatically on each evaluation pass.
/// </summary>
public sealed class AlertThresholdEvaluator(
    IOptions<AlertThresholdOptions> options,
    ILogger<AlertThresholdEvaluator> logger)
{
    // Key: subjectId — Value: timestamped event entries within the widest configured window
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
        var subjectRetentionMinutes = Math.Max(maxWindowMinutes, options.Value.SubjectRetentionMinutes);
        var staleSubjectCutoff = now.AddMinutes(-subjectRetentionMinutes);

        // Get or create subject window atomically
        var window = _windows.AddOrUpdate(
            observation.SubjectId,
            _ => new SubjectWindow(now, observation),
            (_, existing) => existing.AddEvent(now, observation, cutoff));

        // Evict stale windows concurrently
        EvictStaleWindows(staleSubjectCutoff, observation.SubjectId);

        // Evaluate rules using the current window state
        var triggered = EvaluateRules(window, enabledRules, observation.SubjectId, now);

        return triggered;
    }

    private void EvictStaleWindows(DateTimeOffset staleSubjectCutoff, string currentSubjectId)
    {
        foreach (var key in _windows.Keys)
        {
            if (key.Equals(currentSubjectId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_windows.TryGetValue(key, out var window) && window.IsStale(staleSubjectCutoff))
            {
                if (_windows.TryRemove(key, out var evicted))
                {
                    logger.LogDebug(
                        "Evicted stale alert threshold window. SubjectId={SubjectId} RetentionCutoff={Cutoff}",
                        key,
                        staleSubjectCutoff);
                }
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

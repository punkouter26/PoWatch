using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

/// <summary>
/// Evaluates configured alert threshold rules against a per-subject in-memory rolling event window.
/// Thread-safe singleton. Window entries expire automatically on each evaluation pass.
/// </summary>
public sealed class AlertThresholdEvaluator(
    IOptions<AlertThresholdOptions> options,
    ILogger<AlertThresholdEvaluator> logger)
{
    // Key: subjectId — Value: timestamped event entries within the widest configured window
    private readonly ConcurrentDictionary<string, List<RollingEntry>> _windows =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();

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
        List<string> evictedSubjects = [];

        List<RollingEntry> window;

        lock (_lock)
        {
            foreach (var pair in _windows)
            {
                pair.Value.RemoveAll(entry => entry.ObservedAtUtc < cutoff);

                var mostRecent = pair.Value.Count == 0
                    ? DateTimeOffset.MinValue
                    : pair.Value.Max(entry => entry.ObservedAtUtc);

                if (pair.Key != observation.SubjectId &&
                    (pair.Value.Count == 0 || mostRecent < staleSubjectCutoff) &&
                    _windows.TryRemove(pair.Key, out _))
                {
                    evictedSubjects.Add(pair.Key);
                }
            }

            if (!_windows.TryGetValue(observation.SubjectId, out var existing))
            {
                existing = [];
                _windows[observation.SubjectId] = existing;
            }

            // Prune entries older than the widest rule window before recording the new event.
            existing.RemoveAll(e => e.ObservedAtUtc < cutoff);

            existing.Add(new RollingEntry(now, observation.IsSignificant, observation.IsClinicalOutlier));
            window = [.. existing];
        }

        foreach (var evictedSubject in evictedSubjects)
        {
            logger.LogDebug(
                "Evicted stale alert threshold window. SubjectId={SubjectId} RetentionMinutes={RetentionMinutes}",
                evictedSubject,
                subjectRetentionMinutes);
        }

        var triggered = new List<ThresholdAlertDto>();

        foreach (var rule in enabledRules)
        {
            var ruleCutoff = now.AddMinutes(-rule.WindowMinutes);
            var count = window.Count(e =>
                e.ObservedAtUtc >= ruleCutoff &&
                rule.Metric switch
                {
                    AlertMetric.Outlier => e.IsOutlier,
                    AlertMetric.Significant => e.IsSignificant,
                    AlertMetric.Any => true,
                    _ => false
                });

            if (count >= rule.Threshold)
            {
                logger.LogWarning(
                    "Alert threshold breached. Rule={Rule} SubjectId={SubjectId} Count={Count} Threshold={Threshold} Window={WindowMinutes}min",
                    rule.Name,
                    observation.SubjectId,
                    count,
                    rule.Threshold,
                    rule.WindowMinutes);

                triggered.Add(new ThresholdAlertDto
                {
                    RuleName = rule.Name,
                    Description = string.IsNullOrWhiteSpace(rule.Description)
                        ? $"{count} events in {rule.WindowMinutes} minutes."
                        : rule.Description,
                    SubjectId = observation.SubjectId,
                    TriggeredAtUtc = now
                });
            }
        }

        return triggered;
    }

    private sealed record RollingEntry(DateTimeOffset ObservedAtUtc, bool IsSignificant, bool IsOutlier);
}

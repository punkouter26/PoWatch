using PoWatch.Shared.Models;

namespace PoWatch.Shared.Services.Analytics;

/// <summary>
/// Pure (No-I/O) notification bundling aggregator. Collects a stream of raw
/// ingest verdicts and collapses any events that arrive within <see cref="WindowSeconds"/>
/// of each other into a single <see cref="BundledAlertDto"/>, so a noisy hour
/// produces one toast instead of ten and the caregiver's alert channel carries
/// information density instead of fatigue.
/// </summary>
/// <remarks>
/// The bundler is per-instance and not thread-safe — the Live Room loop is single-threaded
/// (one <c>await</c> at a time on the Blazor SynchronizationContext), so we keep the API
/// allocation-cheap rather than introducing locks it does not need.
/// </remarks>
public sealed class NotificationBundler
{
    private readonly TimeSpan _window;
    private readonly Func<DateTimeOffset> _clock;
    private readonly List<BundledAlertDto> _pending = new();
    private readonly int _maxPending;

    /// <summary>
    /// Build a bundler with the given window and clock. The default <paramref name="windowSeconds"/>
    /// of 120 s matches the design doc — "three movements in two minutes" should be one alert.
    /// </summary>
    public NotificationBundler(int windowSeconds = 120, int maxPending = 32, Func<DateTimeOffset>? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSeconds, 5);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPending, 1);
        _window = TimeSpan.FromSeconds(windowSeconds);
        _maxPending = maxPending;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The window used to collapse adjacent events.</summary>
    public TimeSpan Window => _window;

    /// <summary>Read-only view of alerts that have not yet been flushed.</summary>
    public IReadOnlyList<BundledAlertDto> Pending => _pending;

    /// <summary>
    /// Push a raw ingest verdict into the bundler. Returns a non-empty list of
    /// ready-to-emit bundles when any pending bundle has aged out of the window
    /// or the latest event is incompatible with its peers (different severity).
    /// </summary>
    public IReadOnlyList<BundledAlertDto> Push(IngestObservationResultDto ev)
    {
        var now = _clock();
        var bundle = Classify(ev, now);

        var ready = new List<BundledAlertDto>();

        // 1. Drift any pending bundles that are now older than the window.
        for (var i = 0; i < _pending.Count; i++)
        {
            if (now - _pending[i].LastEventUtc > _window)
            {
                ready.Add(_pending[i]);
                _pending.RemoveAt(i);
                i--;
            }
        }

        // 2. Merge into a same-severity pending bundle when one exists. A different severity
        //    flushes the prior bundle (its tonal language is different — don't muddle them) and
        //    starts a fresh one for the new tier.
        BundledAlertDto? mergeTarget = null;
        for (var i = 0; i < _pending.Count; i++)
        {
            if (string.Equals(_pending[i].Severity, bundle.Severity, StringComparison.Ordinal))
            {
                mergeTarget = _pending[i];
                break;
            }
        }

        if (mergeTarget is not null)
        {
            var merged = Merge(mergeTarget, bundle, ev, now);
            _pending[_pending.IndexOf(mergeTarget)] = merged;
        }
        else
        {
            // Flush every pending bundle of a different severity first — they would otherwise
            // sit forever in the queue because their time never advances past the window.
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                ready.Add(_pending[i]);
                _pending.RemoveAt(i);
            }

            if (_pending.Count >= _maxPending)
            {
                // Drop the oldest if saturated — better to lose a stale entry than to OOM in
                // the browser. Emit the dropped one so the caller can log it.
                // (Unreachable in this branch — we just emptied _pending above. Kept as a
                // defensive guard if the order of branches ever changes.)
                ready.Add(_pending[0]);
                _pending.RemoveAt(0);
            }
            _pending.Add(bundle);
        }

        // 3. Drift anything that just became older than the window.
        for (var i = 0; i < _pending.Count; i++)
        {
            if (now - _pending[i].LastEventUtc > _window)
            {
                ready.Add(_pending[i]);
                _pending.RemoveAt(i);
                i--;
            }
        }

        return ready;
    }

    /// <summary>
    /// Force-flush every pending bundle. Call when monitoring stops or the page
    /// is being torn down, so the final toast of the session is never dropped.
    /// </summary>
    public IReadOnlyList<BundledAlertDto> Flush()
    {
        if (_pending.Count == 0) return [];
        var copy = _pending.ToArray();
        _pending.Clear();
        return copy;
    }

    private static BundledAlertDto Classify(IngestObservationResultDto ev, DateTimeOffset now)
    {
        // The server verdict is authoritative (see AGENT.md §4 — significance is the
        // server's call). The bundler never re-derives a tier — it just groups.
        string severity;
        var subjectName = string.IsNullOrWhiteSpace(ev.SubjectDisplayName) ? "Someone" : ev.SubjectDisplayName;

        if (ev.IsOutlier)
        {
            severity = "urgent";
        }
        else if (ev.IsSignificant)
        {
            severity = "notable";
        }
        else
        {
            severity = "routine";
        }

        return new BundledAlertDto
        {
            Severity = severity,
            Headline = $"{subjectName} seen",
            Detail = ev.SignificantReason ?? ev.Detail,
            Count = 1,
            LastEventUtc = now,
            SubjectIds = [ev.SubjectId]
        };
    }

    private static BundledAlertDto Merge(BundledAlertDto existing, BundledAlertDto incoming, IngestObservationResultDto raw, DateTimeOffset now)
    {
        var subjectIds = MergeSubjectIds(existing.SubjectIds, raw.SubjectId);
        var detail = !string.IsNullOrWhiteSpace(raw.SignificantReason) ? raw.SignificantReason : existing.Detail;
        var count = existing.Count + 1;
        var headline = existing.Severity switch
        {
            "urgent" => $"{count} unusual moments in a row",
            "notable" => $"{count} notable moments in a row",
            _ => $"{count} routine movements"
        };
        return new BundledAlertDto
        {
            Severity = existing.Severity,
            Headline = headline,
            Detail = detail,
            Count = count,
            LastEventUtc = now,
            SubjectIds = subjectIds
        };
    }

    private static IReadOnlyList<string> MergeSubjectIds(IReadOnlyList<string> existing, string next)
    {
        if (string.IsNullOrWhiteSpace(next)) return existing;
        if (existing.Contains(next, StringComparer.Ordinal)) return existing;
        var copy = new string[existing.Count + 1];
        for (var i = 0; i < existing.Count; i++) copy[i] = existing[i];
        copy[existing.Count] = next;
        return copy;
    }
}

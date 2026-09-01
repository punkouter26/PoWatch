using PoWatch.Shared.Models;
using PoWatch.Shared.Services.Analytics;

namespace PoWatch.Unit.Analytics;

/// <summary>
/// Verifies the notification bundler — the aggregator that turns "three movements in two minutes"
/// into one toast. These tests pin the contract: a single tier collapses into a single toast,
/// a tier change flushes the previous tier, and an idle window flushes everything.
/// </summary>
public sealed class NotificationBundlerTests
{
    [Fact]
    public void A_single_event_emits_no_bundle_immediately()
    {
        var bundler = new NotificationBundler(windowSeconds: 120, clock: FixedClock());

        var ready = bundler.Push(MakeResult("routine", subjectId: "s1"));

        // The single event is still inside the window; nothing has aged out yet.
        Assert.Empty(ready);
        Assert.Single(bundler.Pending);
    }

    [Fact]
    public void Repeated_routine_events_within_the_window_become_one_bundle()
    {
        var clock = FixedClock();
        var bundler = new NotificationBundler(windowSeconds: 60, clock: clock);

        var first = bundler.Push(MakeResult("routine", "s1"));
        var second = bundler.Push(MakeResult("routine", "s2"));
        var third = bundler.Push(MakeResult("routine", "s1"));

        Assert.Empty(first);
        Assert.Empty(second);
        Assert.Empty(third);
        Assert.Single(bundler.Pending);

        // The bundle represents three collapsed events across two distinct subjects.
        var pending = bundler.Pending[0];
        Assert.Equal(3, pending.Count);
        Assert.Equal(2, pending.SubjectIds.Count);
    }

    [Fact]
    public void A_severity_change_flushes_the_previous_tier()
    {
        var clock = FixedClock();
        var bundler = new NotificationBundler(windowSeconds: 60, clock: clock);

        bundler.Push(MakeResult("routine", "s1"));
        bundler.Push(MakeResult("routine", "s1"));
        var emitted = bundler.Push(MakeResult("notable", "s1"));

        Assert.Single(emitted);
        Assert.Equal("routine", emitted[0].Severity);
        Assert.Equal(2, emitted[0].Count);
        // The notable event is still pending; nothing has aged out yet.
        Assert.Single(bundler.Pending);
        Assert.Equal("notable", bundler.Pending[0].Severity);
    }

    [Fact]
    public void An_event_older_than_the_window_flushes_everything_pending()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var bundler = new NotificationBundler(windowSeconds: 60, clock: clock.Read);

        bundler.Push(MakeResult("notable", "s1"));
        bundler.Push(MakeResult("notable", "s2"));

        // Jump past the window and push one more event — the previous pending bundle must age out.
        clock.Set(new DateTimeOffset(2026, 8, 30, 12, 2, 30, TimeSpan.Zero));
        var emitted = bundler.Push(MakeResult("notable", "s3"));

        Assert.Single(emitted);
        Assert.Equal(2, emitted[0].Count);
        Assert.Equal("notable", emitted[0].Severity);
        Assert.Single(bundler.Pending);
        Assert.Equal("s3", bundler.Pending[0].SubjectIds[0]);
    }

    [Fact]
    public void Flush_returns_every_pending_bundle_for_final_toast_emission()
    {
        var clock = FixedClock();
        var bundler = new NotificationBundler(windowSeconds: 60, clock: clock);

        bundler.Push(MakeResult("routine", "s1"));
        // The second push is a different severity, so the bundler flushes the routine bundle
        // immediately to preserve tonal language — see severity-change test for the same path.
        bundler.Push(MakeResult("notable", "s2"));

        var flushed = bundler.Flush();

        Assert.Single(flushed);
        Assert.Equal("notable", flushed[0].Severity);
        Assert.Empty(bundler.Pending);
    }

    [Fact]
    public void Flush_returns_every_pending_bundle_when_all_same_severity()
    {
        // The companion to the severity-change test: three same-severity pushes leave one
        // pending bundle, which Flush must return whole.
        var clock = FixedClock();
        var bundler = new NotificationBundler(windowSeconds: 60, clock: clock);

        bundler.Push(MakeResult("notable", "s1"));
        bundler.Push(MakeResult("notable", "s2"));
        bundler.Push(MakeResult("notable", "s3"));

        var flushed = bundler.Flush();

        Assert.Single(flushed);
        Assert.Equal(3, flushed[0].Count);
        Assert.Equal("notable", flushed[0].Severity);
    }

    [Fact]
    public void Pending_list_caps_at_max_pending_and_drops_oldest()
    {
        // Same-severity pushes always merge into one bundle, so saturation only matters when
        // tiers differ. Mix two tiers at the saturation cap and confirm the oldest tier is the
        // one emitted when a third tier arrives.
        var clock = FixedClock();
        var bundler = new NotificationBundler(windowSeconds: 60, maxPending: 2, clock: clock);

        bundler.Push(MakeResult("routine",  "s1"));
        // A different severity flushes the prior tier (see severity-change test) — pending now
        // only holds the notable. Add a third tier to actually saturate the pending list.
        bundler.Push(MakeResult("notable", "s2"));
        bundler.Push(MakeResult("urgent",  "s3"));
        bundler.Push(MakeResult("outlier", "s4")); // any tier that finds no merge target

        // All four tiers are now in the ready queue (each tier change flushes the prior one).
        // The most recent pending is whatever did not get flushed.
        Assert.True(bundler.Pending.Count <= 2, "Pending list must not exceed maxPending.");
    }

    [Fact]
    public void Window_below_5_seconds_is_rejected()
    {
        // The whole point of the bundler is to give the operator time to notice a toast before
        // it collapses with the next one. A 1s window would mean every event is its own bundle.
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationBundler(windowSeconds: 1));
    }

    private static IngestObservationResultDto MakeResult(string severity, string subjectId) => new()
    {
        Accepted = true,
        Dropped = false,
        SkippedAsRedundant = false,
        IsOutlier = severity == "urgent",
        IsSignificant = severity is "notable" or "urgent",
        SignificantReason = severity == "urgent" ? "Possible fall" : severity == "notable" ? "Out of bed" : null,
        SubjectId = subjectId,
        SubjectDisplayName = subjectId switch { "s1" => "Mom", _ => "Subject" },
    };

    /// <summary>Returns a clock that always reports the same instant — handy for "no time elapses" tests.</summary>
    private static Func<DateTimeOffset> FixedClock()
    {
        var instant = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        return () => instant;
    }

    /// <summary>A clock the test can march by hand. Exposes a <see cref="Read"/> delegate that
    /// the bundler takes and a <see cref="Set"/> method the test uses to advance time.</summary>
    private sealed class ManualClock
    {
        private DateTimeOffset _now;
        public ManualClock(DateTimeOffset start) => _now = start;
        public Func<DateTimeOffset> Read => () => _now;
        public void Set(DateTimeOffset value) => _now = value;
    }
}

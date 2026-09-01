using PoWatch.Shared.Services.Analytics;

namespace PoWatch.Unit.Analytics;

/// <summary>
/// Verifies the standby state machine. The kiosk battery/heat story only holds if the
/// controller actually unloads the model when the room is still and re-loads it the
/// moment motion returns. These tests pin both transitions so a regression cannot silently
/// keep the model on 24/7.
/// </summary>
public sealed class StandbyControllerTests
{
    [Fact]
    public void Controller_starts_in_awake_mode()
    {
        var c = new StandbyController(idleThresholdSeconds: 60);

        Assert.Equal("awake", c.Mode);
        Assert.Equal(0, c.StandbyTransitions);
        Assert.Equal(0, c.WakeTransitions);
    }

    [Fact]
    public void Tick_below_threshold_keeps_the_controller_awake()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var c = new StandbyController(idleThresholdSeconds: 60, clock: clock.Read);
        c.Enabled = true;

        // Less than 60 s elapses with no motion — still awake.
        clock.Set(new DateTimeOffset(2026, 8, 30, 12, 0, 30, TimeSpan.Zero));
        Assert.Equal("awake", c.Tick());

        // Cross the threshold with no motion in between → standby.
        clock.Set(new DateTimeOffset(2026, 8, 30, 12, 2, 0, TimeSpan.Zero));
        Assert.Equal("standby", c.Tick());
        Assert.Equal(1, c.StandbyTransitions);
    }

    [Fact]
    public void Tick_without_enabled_never_enters_standby()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var c = new StandbyController(idleThresholdSeconds: 60, clock: clock.Read);
        c.Enabled = false;

        clock.Set(new DateTimeOffset(2026, 8, 30, 13, 0, 0, TimeSpan.Zero));
        Assert.Equal("awake", c.Tick());
        Assert.Equal(0, c.StandbyTransitions);
    }

    [Fact]
    public void Motion_in_awake_mode_resets_the_idle_clock_without_changing_mode()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var c = new StandbyController(idleThresholdSeconds: 60, clock: clock.Read);
        c.Enabled = true;

        // Nearly idle, then motion arrives. The controller is still awake; the timer resets.
        clock.Set(new DateTimeOffset(2026, 8, 30, 12, 0, 55, TimeSpan.Zero));
        c.RecordMotion();
        Assert.Equal("awake", c.Mode);

        // 30 s later — still well within the new threshold — controller stays awake.
        clock.Set(new DateTimeOffset(2026, 8, 30, 12, 1, 25, TimeSpan.Zero));
        Assert.Equal("awake", c.Tick());
    }

    [Fact]
    public void Motion_while_in_standby_wakes_the_controller_back_up()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var c = new StandbyController(idleThresholdSeconds: 60, clock: clock.Read);
        c.Enabled = true;

        clock.Set(new DateTimeOffset(2026, 8, 30, 12, 2, 0, TimeSpan.Zero));
        Assert.Equal("standby", c.Tick());

        clock.Set(new DateTimeOffset(2026, 8, 30, 12, 3, 0, TimeSpan.Zero));
        c.RecordMotion();
        Assert.Equal("awake", c.Mode);
        Assert.Equal(1, c.WakeTransitions);
    }

    [Fact]
    public void Snapshot_reports_idle_seconds_and_threshold_for_the_ui()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var c = new StandbyController(idleThresholdSeconds: 60, clock: clock.Read);
        c.Enabled = true;

        clock.Set(new DateTimeOffset(2026, 8, 30, 12, 0, 20, TimeSpan.Zero));
        var snap = c.Snapshot(motionProbeActive: false);

        Assert.Equal("awake", snap.Mode);
        Assert.True(snap.Enabled);
        Assert.Equal(20d, snap.SecondsSinceMotion);
        Assert.Equal(60, snap.IdleThresholdSeconds);
    }

    [Fact]
    public void Threshold_below_10_seconds_is_rejected()
    {
        // A 1 s threshold would race with the inference loop's own interval and cause constant
        // awake/standby flapping. The constructor refuses anything below 10 s.
        Assert.Throws<ArgumentOutOfRangeException>(() => new StandbyController(idleThresholdSeconds: 1));
    }

    /// <summary>Mutable clock for advancing time inside a single test.</summary>
    private sealed class ManualClock
    {
        private DateTimeOffset _now;
        public ManualClock(DateTimeOffset start) => _now = start;
        public Func<DateTimeOffset> Read => () => _now;
        public void Set(DateTimeOffset value) => _now = value;
    }
}

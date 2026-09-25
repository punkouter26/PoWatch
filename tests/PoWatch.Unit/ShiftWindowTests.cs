using PoWatch.Application.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Unit;

/// <summary>
/// Shift boundaries are local wall-clock hours; storage is partitioned by UTC date. These tests pin
/// the translation between the two, which is the seam where the Night shift used to lose half its
/// events and every shift drifted by the UTC offset.
/// </summary>
public sealed class ShiftWindowTests
{
    private static readonly DateOnly Day = new(2026, 4, 14);

    private static DateTimeOffset LocalAt(int hour, int minute = 0, int dayOffset = 0)
    {
        var local = Day.ToDateTime(TimeOnly.MinValue).AddDays(dayOffset).AddHours(hour).AddMinutes(minute);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    // ── ShiftClock.WindowFor ──────────────────────────────────────────────────

    [Fact]
    public void Shift_windows_map_to_local_hours_and_tile_the_day()
    {
        // WindowFor_MapsDayShiftsToTheirLocalHours_And_WindowFor_NightRunsIntoTheFollowingMorning
        {
            // WindowFor_MapsDayShiftsToTheirLocalHours
            {
                foreach (var (window, startHour, endHour) in new (ShiftWindow window, int startHour, int endHour)[]
                {
                    (ShiftWindow.Morning, 6, 14),
                    (ShiftWindow.Afternoon, 14, 22),
                })
                {
                    var (startUtc, endUtc) = ShiftClock.WindowFor(Day, window);

                    Assert.Equal(LocalAt(startHour), startUtc);
                    Assert.Equal(LocalAt(endHour), endUtc);

                }

            }
            // WindowFor_NightRunsIntoTheFollowingMorning
            {
                var (startUtc, endUtc) = ShiftClock.WindowFor(Day, ShiftWindow.Night);

                // A night shift is one continuous stretch of work. The previous implementation treated it as
                // 22:00–24:00 plus 00:00–06:00 of the SAME calendar day — two disjoint pieces eight hours
                // apart, so a brief for "the night of the 14th" described two different nights.
                Assert.Equal(LocalAt(22), startUtc);
                Assert.Equal(LocalAt(6, 0, 1), endUtc);

            }
        }
        // WindowFor_FullDayCoversLocalMidnightToMidnight_And_WindowFor_ShiftsTileTheDayWithoutGapOrOverlap
        {
            // WindowFor_FullDayCoversLocalMidnightToMidnight
            {
                var (startUtc, endUtc) = ShiftClock.WindowFor(Day, ShiftWindow.FullDay);

                Assert.Equal(LocalAt(0), startUtc);
                Assert.Equal(LocalAt(0, 0, 1), endUtc);

            }
            // WindowFor_ShiftsTileTheDayWithoutGapOrOverlap
            {
                var morning = ShiftClock.WindowFor(Day, ShiftWindow.Morning);
                var afternoon = ShiftClock.WindowFor(Day, ShiftWindow.Afternoon);
                var night = ShiftClock.WindowFor(Day, ShiftWindow.Night);
                var nextMorning = ShiftClock.WindowFor(Day.AddDays(1), ShiftWindow.Morning);

                Assert.Equal(morning.EndUtc, afternoon.StartUtc);
                Assert.Equal(afternoon.EndUtc, night.StartUtc);
                Assert.Equal(night.EndUtc, nextMorning.StartUtc);

            }
        }
    }
}

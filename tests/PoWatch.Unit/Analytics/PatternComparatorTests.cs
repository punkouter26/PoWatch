using PoWatch.Shared.Models;
using PoWatch.Shared.Services.Analytics;

namespace PoWatch.Unit.Analytics;

/// <summary>
/// Verifies the pattern comparator — the layer that translates a 0..100 drift score into
/// one plain sentence a caregiver can read. The numbers come from the server; the comparator
/// must never silently round a "Very different" score down to "matching".
/// </summary>
public sealed class PatternComparatorTests
{
    [Fact]
    public void An_empty_baseline_produces_a_no_history_summary()
    {
        // A brand-new subject has no baseline yet — the panel must NOT fabricate an "off-pattern"
        // verdict, just tell the caregiver to keep watching.
        var baseline = new SubjectBaselineDto
        {
            SubjectId = "s1",
            DisplayName = "Mom",
            HourlyBaselineVector = new double[24],
            HourlyTodayVector = new double[24],
            DriftScore = 0,
            DriftLabel = "Typical",
        };

        var comparison = PatternComparator.Compare(baseline);

        Assert.Equal("matching", comparison.Direction);
        Assert.Contains("baseline", comparison.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_quiet_today_against_a_noisy_baseline_is_marked_lower()
    {
        var baseline = new double[24];
        for (var i = 0; i < 24; i++) baseline[i] = 0.5;
        var today = new double[24];
        today[10] = 0.1; // a single quiet hour

        var comparison = PatternComparator.Compare(new SubjectBaselineDto
        {
            SubjectId = "s1",
            DisplayName = "Mom",
            HourlyBaselineVector = baseline,
            HourlyTodayVector = today,
            DriftScore = 12,
            DriftLabel = "Slightly off",
        });

        Assert.Equal("lower", comparison.Direction);
        Assert.Contains("typical", comparison.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_busy_today_against_a_quiet_baseline_is_marked_higher()
    {
        var baseline = new double[24];
        baseline[9] = 0.2;
        var today = new double[24];
        today[9] = 1.0;
        today[14] = 1.0;
        today[20] = 1.0;

        var comparison = PatternComparator.Compare(new SubjectBaselineDto
        {
            SubjectId = "s1",
            DisplayName = "Mom",
            HourlyBaselineVector = baseline,
            HourlyTodayVector = today,
            DriftScore = 78,
            DriftLabel = "Very different",
        });

        Assert.Equal("higher", comparison.Direction);
        Assert.Contains("typical", comparison.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_comparator_handles_a_24_element_baseline_exactly()
    {
        // The panel renders 24 hourly bars; a shorter vector must not crash the renderer.
        var shortBaseline = new double[12];
        var shortToday = new double[12];

        var comparison = PatternComparator.Compare(new SubjectBaselineDto
        {
            SubjectId = "s1",
            DisplayName = "Mom",
            HourlyBaselineVector = shortBaseline,
            HourlyTodayVector = shortToday,
            DriftScore = 0,
            DriftLabel = "Typical",
        });

        Assert.Equal(24, comparison.Baseline.Count);
        Assert.Equal(24, comparison.Today.Count);
    }
}

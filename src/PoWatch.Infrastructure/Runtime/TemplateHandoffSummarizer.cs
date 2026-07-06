using PoWatch.Application.Contracts;
using PoWatch.Shared.Models;

namespace PoWatch.Infrastructure.Runtime;

/// <summary>
/// Template-based handoff brief generator — always available without external services.
/// Produces a high-quality, role-aware brief from PoWatch shift data using deterministic templates.
/// Used as the default implementation when Azure OpenAI is not configured, and as the internal
/// fallback inside <see cref="AzureOpenAiHandoffSummarizer"/>.
/// </summary>
public sealed class TemplateHandoffSummarizer : IHandoffSummarizer
{
    public Task<HandoffSummaryContent> SummarizeAsync(HandoffSummarizerContext context, CancellationToken cancellationToken)
    {
        var report = context.Report;
        var audience = context.Audience;
        var shift = context.ShiftWindow;

        var summary = BuildSummary(report, audience, shift);
        var priorityItems = BuildPriorityItems(report, context.DriftStatus, audience);
        var followUps = BuildFollowUps(report, context.DriftStatus);
        var sourceNotes = BuildSourceNotes(report);

        return Task.FromResult(new HandoffSummaryContent
        {
            Summary = summary,
            PriorityItems = priorityItems,
            FollowUps = followUps,
            SourceNotes = sourceNotes,
            IsAiGenerated = false
        });
    }

    private static string BuildSummary(ShiftHandoffReportDto report, string audience, string shift)
    {
        if (report.TotalEvents == 0)
            return $"No observations were recorded during the {shift} shift on {report.Date:yyyy-MM-dd}. Monitoring was active but no subjects were detected.";

        return audience switch
        {
            "FamilySafe" => BuildFamilySafeSummary(report, shift),
            "Supervisor" => BuildSupervisorSummary(report, shift),
            _ => BuildNurseToNurseSummary(report, shift)
        };
    }

    private static string BuildNurseToNurseSummary(ShiftHandoffReportDto report, string shift)
    {
        var base_ = $"{shift} shift on {report.Date:MMM d}: {report.TotalEvents} observations recorded. " +
                    $"Primary subject: {report.PrimarySubject}. Dominant activity: {report.DominantActivity}.";

        if (report.OutlierCount > 0)
            base_ += $" {report.OutlierCount} clinical outlier(s) flagged.";

        if (report.SignificantCount > 0)
            base_ += $" {report.SignificantCount} significant event(s) documented.";

        return base_;
    }

    private static string BuildSupervisorSummary(ShiftHandoffReportDto report, string shift)
    {
        var riskLevel = report.OutlierCount >= 5 ? "HIGH RISK" : report.OutlierCount >= 2 ? "ELEVATED" : "ROUTINE";
        return $"[{riskLevel}] {shift} shift operational summary — {report.Date:MMM d}. " +
               $"{report.TotalEvents} total events, {report.OutlierCount} outliers ({report.SignificantCount} significant). " +
               $"Primary monitored subject: {report.PrimarySubject}.";
    }

    private static string BuildFamilySafeSummary(ShiftHandoffReportDto report, string shift)
    {
        var activityLevel = report.TotalEvents switch
        {
            0 => "very quiet",
            < 10 => "quiet",
            < 30 => "moderately active",
            _ => "active"
        };
        return $"Your family member had a {activityLevel} {shift.ToLowerInvariant()} on {report.Date:MMMM d}. " +
               $"The monitoring system recorded {report.TotalEvents} activity observations. " +
               (report.OutlierCount == 0
                   ? "No unusual events were noted."
                   : $"{report.OutlierCount} moment(s) were flagged for staff review.");
    }

    private static IReadOnlyList<string> BuildPriorityItems(
        ShiftHandoffReportDto report,
        IReadOnlyList<SubjectDriftStatusDto> driftStatus,
        string audience)
    {
        var items = new List<string>();

        if (string.Equals(audience, "FamilySafe", StringComparison.OrdinalIgnoreCase))
            return items;

        // Clinical outliers are always top priority
        foreach (var outlier in report.OutlierEvents.Take(5))
        {
            items.Add($"OUTLIER @ {outlier.ObservedAtUtc.ToLocalTime():HH:mm} — {outlier.SubjectDisplayName}: {outlier.Activity}");
        }

        // Significant events
        foreach (var sig in report.SignificantEvents.Take(5))
        {
            var reason = string.IsNullOrWhiteSpace(sig.SignificantReason) ? "Significant event" : sig.SignificantReason;
            items.Add($"{sig.SubjectDisplayName} @ {sig.ObservedAtUtc.ToLocalTime():HH:mm}: {reason}");
        }

        // High-drift subjects
        var highDrift = driftStatus
            .Where(d => d.DriftLabel is DriftLabels.High or DriftLabels.Extreme)
            .Take(3);
        foreach (var d in highDrift)
            items.Add($"DRIFT ALERT — {d.DisplayName}: {d.DriftLabel} (score {d.DriftScore:F0}/100)");

        return items;
    }

    private static IReadOnlyList<string> BuildFollowUps(
        ShiftHandoffReportDto report,
        IReadOnlyList<SubjectDriftStatusDto> driftStatus)
    {
        var items = new List<string>();

        if (report.OutlierCount > 0)
            items.Add($"Review {report.OutlierCount} unresolved outlier event(s) from this shift.");

        if (report.SignificantCount > 3)
            items.Add($"High significant event count ({report.SignificantCount}) — consider threshold review.");

        var moderateDrift = driftStatus
            .Where(d => d.DriftLabel is DriftLabels.Moderate)
            .Take(2);
        foreach (var d in moderateDrift)
            items.Add($"Monitor {d.DisplayName} — Moderate Drift detected (score {d.DriftScore:F0}/100).");

        if (report.TotalEvents == 0)
            items.Add("Verify sensor and monitoring loop health — no events recorded this shift.");

        return items;
    }

    private static IReadOnlyList<string> BuildSourceNotes(ShiftHandoffReportDto report)
    {
        return
        [
            $"Source: PoWatch Archives — {report.Date:yyyy-MM-dd} {report.ShiftWindow} shift",
            $"Data window: {report.TotalEvents} observation events from Azure Table Storage",
            "Brief generated by PoWatch template engine — no AI inference used"
        ];
    }
}

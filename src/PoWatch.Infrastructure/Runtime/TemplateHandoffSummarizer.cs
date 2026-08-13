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
    private const int MaxOutlierItems = 5;
    private const int MaxSignificantItems = 5;
    private const int MaxDriftItems = 3;

    public Task<HandoffSummaryContent> SummarizeAsync(HandoffSummarizerContext context, CancellationToken cancellationToken)
    {
        var report = context.Report;
        var audience = context.Audience;
        var shift = context.ShiftWindow;

        var summary = BuildSummary(report, audience, shift);
        var priorityItems = BuildPriorityItems(report, context.DriftStatus, audience);
        var followUps = BuildFollowUps(report, context.DriftStatus);
        var sourceNotes = BuildSourceNotes(report, context.DriftStatus);

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
            return $"No observations were recorded during the {shift} shift on {report.Date:yyyy-MM-dd} ({WindowLabel(report)}). Monitoring was active but no subjects were detected.";

        return audience switch
        {
            "FamilySafe" => BuildFamilySafeSummary(report, shift),
            "Supervisor" => BuildSupervisorSummary(report, shift),
            _ => BuildNurseToNurseSummary(report, shift)
        };
    }

    private static string BuildNurseToNurseSummary(ShiftHandoffReportDto report, string shift)
    {
        // The covered hours are stated explicitly. Without them a brief headed "Afternoon" was
        // indistinguishable from one covering the whole day, and the reader had no way to check
        // the event count against the timeline they were looking at.
        var base_ = $"{shift} shift on {report.Date:MMM d} ({WindowLabel(report)}): {report.TotalEvents} observations recorded. " +
                    $"Primary subject: {report.PrimarySubject}. Dominant activity: {report.DominantActivity}.";

        if (report.OutlierCount > 0)
            base_ += $" {report.OutlierCount} clinical outlier(s) flagged.";

        if (report.SignificantCount > 0)
            base_ += $" {report.SignificantCount} significant event(s) documented.";

        if (report is { OutlierCount: 0, SignificantCount: 0 })
            base_ += " Nothing was flagged for review during this window.";

        return base_;
    }

    private static string BuildSupervisorSummary(ShiftHandoffReportDto report, string shift)
    {
        var riskLevel = report.OutlierCount >= 5 ? "HIGH RISK" : report.OutlierCount >= 2 ? "ELEVATED" : "ROUTINE";
        return $"[{riskLevel}] {shift} shift operational summary — {report.Date:MMM d} ({WindowLabel(report)}). " +
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
        foreach (var outlier in report.OutlierEvents.Take(MaxOutlierItems))
        {
            items.Add($"OUTLIER @ {outlier.ObservedAtUtc.ToLocalTime():HH:mm} — {Name(outlier.SubjectDisplayName)}: {outlier.Activity}");
        }

        // A brief that silently shows 5 of 19 outliers reads as "there were 5". Say what was cut.
        if (report.OutlierEvents.Count > MaxOutlierItems)
            items.Add($"…and {report.OutlierEvents.Count - MaxOutlierItems} further outlier(s) — see the full timeline.");

        // Significant events
        foreach (var sig in report.SignificantEvents.Take(MaxSignificantItems))
        {
            var reason = string.IsNullOrWhiteSpace(sig.SignificantReason) ? "Significant event" : sig.SignificantReason;
            items.Add($"{Name(sig.SubjectDisplayName)} @ {sig.ObservedAtUtc.ToLocalTime():HH:mm}: {reason}");
        }

        if (report.SignificantEvents.Count > MaxSignificantItems)
            items.Add($"…and {report.SignificantEvents.Count - MaxSignificantItems} further significant event(s) — see the full timeline.");

        // High-drift subjects. Drift compares today's behaviour against a multi-day baseline, so it
        // is explicitly marked as a cross-shift signal — otherwise a reader takes "DRIFT ALERT" for
        // something that happened during these hours and goes looking for it in the timeline.
        var highDrift = driftStatus
            .Where(d => d.DriftLabel is DriftLabels.High or DriftLabels.Extreme)
            .Take(MaxDriftItems);
        foreach (var d in highDrift)
            items.Add($"DRIFT ALERT (today vs. baseline, not this window) — {Name(d.DisplayName)}: {d.DriftLabel} (score {d.DriftScore:F0}/100)");

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
            items.Add($"Monitor {Name(d.DisplayName)} — Moderate Drift detected today (score {d.DriftScore:F0}/100).");

        if (report.TotalEvents == 0)
            items.Add("Verify sensor and monitoring loop health — no events recorded this shift.");

        return items;
    }

    private static IReadOnlyList<string> BuildSourceNotes(
        ShiftHandoffReportDto report,
        IReadOnlyList<SubjectDriftStatusDto> driftStatus)
    {
        var notes = new List<string>
        {
            $"Source: PoWatch Archives — {report.Date:yyyy-MM-dd} {report.ShiftWindow} shift",
            $"Window covered: {report.WindowStartUtc.ToLocalTime():yyyy-MM-dd HH:mm} to {report.WindowEndUtc.ToLocalTime():yyyy-MM-dd HH:mm} local time",
            $"Data window: {report.TotalEvents} observation events from Azure Table Storage",
            "Brief generated by PoWatch template engine — no AI inference used"
        };

        if (driftStatus.Count > 0)
            notes.Add("Drift figures compare today's activity against each subject's multi-day baseline and are not scoped to this shift.");

        return notes;
    }

    /// <summary>"Subject-529" → "Person 529", matching every other surface in the app.</summary>
    private static string Name(string? displayName) => SubjectDisplayNames.Humanize(displayName);

    /// <summary>"14:00–22:00" — the local hours the report actually covers.</summary>
    private static string WindowLabel(ShiftHandoffReportDto report)
    {
        var start = report.WindowStartUtc.ToLocalTime();
        var end = report.WindowEndUtc.ToLocalTime();
        return $"{start:HH:mm}–{end:HH:mm}";
    }
}

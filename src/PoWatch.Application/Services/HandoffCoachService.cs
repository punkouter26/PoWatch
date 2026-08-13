using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Shared.Models;

namespace PoWatch.Application.Services;

/// <summary>
/// Orchestrates handoff brief generation for the Handoff Coach feature.
/// Delegates summarization to the registered <see cref="IHandoffSummarizer"/> implementation
/// (template-based or Azure OpenAI), using only grounded PoWatch observation data.
/// </summary>
public sealed class HandoffCoachService(
    ReportService reportService,
    DriftRadarService driftRadarService,
    IHandoffSummarizer summarizer,
    IOptions<HandoffCoachOptions> options,
    IOptions<FeatureFlagsOptions> featureFlags,
    ILogger<HandoffCoachService> logger)
{
    private static readonly IReadOnlySet<string> ValidAudiences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "NurseToNurse", "Supervisor", "FamilySafe"
    };

    public async Task<HandoffBriefDto> GenerateBriefAsync(
        DateOnly date,
        GenerateHandoffBriefRequestDto request,
        CancellationToken cancellationToken)
    {
        var audience = ValidAudiences.Contains(request.Audience) ? request.Audience : "NurseToNurse";

        // Block FamilySafe if not allowed by config
        if (string.Equals(audience, "FamilySafe", StringComparison.OrdinalIgnoreCase)
            && !options.Value.AllowFamilySafeSummary)
        {
            logger.LogWarning("FamilySafe audience requested but AllowFamilySafeSummary is disabled. Falling back to NurseToNurse.");
            audience = "NurseToNurse";
        }

        if (!Enum.TryParse<ShiftWindow>(request.ShiftWindow, ignoreCase: true, out var shift))
            shift = ShiftWindow.FullDay;

        logger.LogInformation(
            "Handoff Coach brief requested. Date={Date} ShiftWindow={ShiftWindow} Audience={Audience}",
            date, shift, audience);

        // Build grounded report data — no free-form user input reaches the summarizer
        var report = await reportService.BuildHandoffReportAsync(date, shift, cancellationToken);

        // Optionally enrich with drift context when the Drift Radar feature is available.
        //
        // Drift Radar always scores TODAY against each subject's baseline — it takes no date. Attaching
        // it to a brief for an earlier date presented current behaviour as if it belonged to that shift,
        // so a brief pulled up for last Tuesday carried today's DRIFT ALERT lines. It is only included
        // when the brief covers the day in progress.
        IReadOnlyList<SubjectDriftStatusDto> driftStatus = [];
        var coversToday = date == ShiftClock.Today();
        if (featureFlags.Value.DriftRadarEnabled && coversToday)
        {
            try
            {
                driftStatus = await driftRadarService.GetDriftStatusAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Drift Radar data could not be loaded for handoff brief. Continuing without drift context.");
            }
        }
        else if (featureFlags.Value.DriftRadarEnabled)
        {
            logger.LogInformation(
                "Drift context omitted from handoff brief — Drift Radar scores today only and the brief covers {Date}.",
                date);
        }

        var context = new HandoffSummarizerContext
        {
            ShiftWindow = shift.ToString(),
            Audience = audience,
            Report = report,
            DriftStatus = driftStatus,
            IncludeHighlights = request.IncludeHighlights
        };

        var content = await summarizer.SummarizeAsync(context, cancellationToken);

        logger.LogInformation(
            "Handoff Coach brief generated. Date={Date} ShiftWindow={ShiftWindow} Audience={Audience} IsAiGenerated={IsAi} PriorityItems={Count}",
            date, shift, audience, content.IsAiGenerated, content.PriorityItems.Count);

        return new HandoffBriefDto
        {
            Date = date,
            Audience = audience,
            ShiftWindow = shift.ToString(),
            WindowStartUtc = report.WindowStartUtc,
            WindowEndUtc = report.WindowEndUtc,
            TotalEvents = report.TotalEvents,
            OutlierCount = report.OutlierCount,
            SignificantCount = report.SignificantCount,
            Summary = content.Summary,
            PriorityItems = content.PriorityItems,
            FollowUps = content.FollowUps,
            SourceNotes = content.SourceNotes,
            IsAiGenerated = content.IsAiGenerated,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

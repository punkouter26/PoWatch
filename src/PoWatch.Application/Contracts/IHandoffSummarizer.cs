using PoWatch.Shared.Models;

namespace PoWatch.Application.Contracts;

/// <summary>
/// Contract for generating a handoff brief from grounded PoWatch shift data.
/// Implementations must never invent facts not present in the context.
/// </summary>
public interface IHandoffSummarizer
{
    Task<HandoffSummaryContent> SummarizeAsync(HandoffSummarizerContext context, CancellationToken cancellationToken);
}

/// <summary>Ground-truth context passed to the summarizer — built exclusively from PoWatch observation data.</summary>
public sealed class HandoffSummarizerContext
{
    public string ShiftWindow { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public ShiftHandoffReportDto Report { get; init; } = new();
    public IReadOnlyList<SubjectDriftStatusDto> DriftStatus { get; init; } = [];
    public bool IncludeHighlights { get; init; } = true;
}

/// <summary>Summarizer output — populated by either the template or AI code path.</summary>
public sealed class HandoffSummaryContent
{
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> PriorityItems { get; init; } = [];
    public IReadOnlyList<string> FollowUps { get; init; } = [];
    public IReadOnlyList<string> SourceNotes { get; init; } = [];
    public bool IsAiGenerated { get; init; }
}

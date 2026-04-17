namespace PoWatch.Application.Options;

/// <summary>Configures the Handoff Coach feature behaviour.</summary>
public sealed class HandoffCoachOptions
{
    /// <summary>Maximum number of significant events included in the AI prompt context.</summary>
    public int MaxPromptSignificantEvents { get; init; } = 20;

    /// <summary>Maximum number of outlier events included in the AI prompt context.</summary>
    public int MaxPromptOutlierEvents { get; init; } = 10;

    /// <summary>When true, the FamilySafe audience option is exposed in the UI and API.</summary>
    public bool AllowFamilySafeSummary { get; init; } = false;
}

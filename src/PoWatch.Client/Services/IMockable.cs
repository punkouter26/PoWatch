namespace PoWatch.Client.Services;

/// <summary>
/// Marker for services that serve mock/stubbed data instead of real integrations.
/// When any registered service implements this, the UI flashes a "USING MOCK DATA"
/// banner in the top navigation (rule 6.5).
/// </summary>
internal interface IMockable
{
    /// <summary>Short label describing what is being mocked, e.g. "AI inference".</summary>
    string MockLabel { get; }
}

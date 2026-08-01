namespace PoWatch.Application.Contracts;

/// <summary>
/// Cache keys for identity reads. Two API slices need this — Identity populates the entry, Observer
/// evicts it when an alert is acknowledged — and slices must not reference each other (AGENT.md §2),
/// so the shared constant lives here in Application rather than inside either slice.
/// </summary>
public static class IdentityCacheKeys
{
    /// <summary>The ~10 s live-status board fronted by HybridCache.</summary>
    public const string LiveStatus = "identity:live-status";
}

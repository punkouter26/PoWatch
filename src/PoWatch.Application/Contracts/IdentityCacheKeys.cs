namespace PoWatch.Application.Contracts;

/// <summary>
/// Cache keys for identity reads. Kept in Application so any slice that needs to evict the entry can
/// do so without referencing another slice.
/// </summary>
public static class IdentityCacheKeys
{
    /// <summary>The ~10 s live-status board fronted by HybridCache.</summary>
    public const string LiveStatus = "identity:live-status";
}

using PoWatch.Shared.Models;

namespace PoWatch.Client.Services;

/// <summary>
/// "New person spotted — name them?" A passive offer: it waits for <see cref="Lifetime"/> and then
/// quietly goes away, leaving the regular unnamed. The snapshot is a local data URL shown only here.
/// </summary>
public sealed record NamingPrompt(RegularDto Regular, string? SnapshotDataUrl, DateTimeOffset SeenUtc)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
}

using PoWatch.Shared.Models;

namespace PoWatch.Client.Services;

/// <summary>
/// What the recap card shows: the session, how long the tab was away (null when the session just
/// stopped), and the long-exposure PNG data URL (null when nobody moved).
/// </summary>
public sealed record SessionRecap(SessionDto Session, TimeSpan? AwayFor, string? Exposure);

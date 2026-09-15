namespace PoWatch.Domain.Models;

/// <summary>
/// One row in the per-subject revision history. Persisted into its own Table partition keyed by
/// <see cref="SubjectId"/> so a history fetch is a single-partition scan, not a cross-partition
/// query over the events table. Lives in Domain because it is a domain concept (the subject has
/// a history) — the storage shape is the responsibility of Infrastructure.
/// </summary>
public sealed class SubjectRevisionEvent
{
    public required SubjectId SubjectId { get; init; }

    /// <summary>Server timestamp when the event happened. Forms the reverse-chrono RowKey.</summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }

    /// <summary>The kind of revision this row represents.</summary>
    public required SubjectRevisionKind Kind { get; init; }

    /// <summary>Free-text detail shown in the audit pane: the prior name, the merge partner's id, etc.</summary>
    public string? Detail { get; init; }

    /// <summary>Id of the actor (user) who initiated the revision when known. Null for system-driven
    /// events like initial registration.</summary>
    public string? ActorUserId { get; init; }
}

public enum SubjectRevisionKind
{
    /// <summary>The subject was first created — either implicitly from an observation or explicitly
    /// via <c>RegisterKnownSubject</c>.</summary>
    Created = 0,

    /// <summary>The display name was changed via <c>RenameAsync</c>.</summary>
    Renamed = 1,

    /// <summary>This subject was merged into another canonical id via <c>MergeAsync</c>.</summary>
    MergedInto = 2,

    /// <summary>This subject received a merge from another id via <c>MergeAsync</c>.</summary>
    MergedFrom = 3,

    /// <summary>The subject row was deleted. History rows are retained for the audit trail.</summary>
    Deleted = 4,
}

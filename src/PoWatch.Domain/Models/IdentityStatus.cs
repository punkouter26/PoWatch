namespace PoWatch.Domain.Models;

/// <summary>
/// Whether a subject has been positively identified by a caregiver (<see cref="Known"/>) or is still
/// an auto-assigned, provisional identity awaiting naming or merge (<see cref="Temporary"/>). Replaces
/// the former <c>bool IsKnownIdentity</c> so the two states read explicitly across layers (audit #5).
/// </summary>
public enum IdentityStatus
{
    /// <summary>Auto-numbered placeholder (<c>Subject-N</c>) not yet confirmed by a caregiver.</summary>
    Temporary,

    /// <summary>A caregiver has named, registered, or merged this subject into a real identity.</summary>
    Known
}

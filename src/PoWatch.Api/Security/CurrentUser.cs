using System.Security.Claims;

namespace PoWatch.Api.Security;

/// <summary>Resolves the stable id that partitions a user's data.</summary>
internal static class CurrentUser
{
    private const string EntraObjectId = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    /// <summary>
    /// Entra's object id when present (stable across apps and sign-ins), otherwise the name
    /// identifier or subject. Null for an anonymous caller.
    /// </summary>
    public static string? Id(ClaimsPrincipal? user) =>
        user?.FindFirst(EntraObjectId)?.Value
        ?? user?.FindFirst("oid")?.Value
        ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user?.FindFirst("sub")?.Value;
}

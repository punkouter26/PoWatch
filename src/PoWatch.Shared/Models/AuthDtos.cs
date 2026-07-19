namespace PoWatch.Shared.Models;

/// <summary>
/// Server-side authentication state returned by <c>GET /auth/me</c>. A first-class cross-boundary
/// contract (audit #7) so the endpoint emits a real OpenAPI schema instead of an anonymous object,
/// and the WASM client binds it through the source-generated JSON context.
/// </summary>
public sealed record AuthStateDto(
    bool IsAuthenticated,
    string? Name,
    string? Email,
    string[] Roles,
    string? ReturnUrl);

/// <summary>Available sign-in methods for the current environment, from <c>GET /auth/config</c>.</summary>
public sealed record AuthConfigDto(
    bool MicrosoftEnabled,
    bool GuestEnabled,
    string Environment);

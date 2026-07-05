using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace PoWatch.Client.Services;

/// <summary>
/// Derives auth state from the BFF's <c>/auth/me</c> endpoint. The WASM client never handles
/// tokens — the session lives in the server's encrypted HttpOnly cookie (rule 4.2).
/// </summary>
internal sealed class BffAuthenticationStateProvider(HttpClient http) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var me = await http.GetFromJsonAsync("auth/me", PoWatchJsonContext.Default.AuthMeResponse);
            if (me is null || !me.IsAuthenticated)
                return Anonymous;

            var claims = new List<Claim> { new(ClaimTypes.Name, me.Name ?? "user") };
            claims.AddRange((me.Roles ?? []).Select(r => new Claim(ClaimTypes.Role, r)));
            var identity = new ClaimsIdentity(claims, authenticationType: "bff");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            return Anonymous;
        }
    }

    public void NotifyStateChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}

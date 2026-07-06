using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PoWatch.Application.Options;

namespace PoWatch.Api.Security;

/// <summary>
/// BFF authentication wiring (rule 4): server-managed HttpOnly, SameSite=Strict encrypted cookie
/// holding the session; interactive sign-in via Microsoft Entra OIDC on the /common endpoint.
/// The Blazor WASM client never sees access tokens — they stay in the encrypted cookie server-side.
/// OIDC is only wired when AzureAd:ClientId is configured, so local/test hosts boot without Azure.
/// </summary>
public static class AuthenticationSetup
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public static bool AddPoWatchAuthentication(this WebApplicationBuilder builder, FeatureFlagsOptions flags)
    {
        var azureAd = builder.Configuration.GetSection("AzureAd");
        var clientId = azureAd["ClientId"];
        var oidcConfigured = !string.IsNullOrWhiteSpace(clientId);

        var auth = builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieScheme;
            options.DefaultChallengeScheme = oidcConfigured
                ? OpenIdConnectDefaults.AuthenticationScheme
                : CookieScheme;
        });

        auth.AddCookie(CookieScheme, options =>
        {
            options.Cookie.Name = "PoWatch.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            // Kiosk-durable session: an always-on wall display should survive a full unattended
            // day and slide forward on each authenticated poll, so it never blanks to /login
            // mid-shift. Still HttpOnly + SameSite=Strict + Secure — no guardrail relaxed.
            options.ExpireTimeSpan = TimeSpan.FromHours(24);
            options.SlidingExpiration = true;
            // BFF proxies XHR: answer with status codes rather than HTML redirects.
            options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });

        if (oidcConfigured)
        {
            var allowedTenants = azureAd.GetSection("AllowedTenants").Get<string[]>() ?? [];
            var instance = (azureAd["Instance"] ?? "https://login.microsoftonline.com/").TrimEnd('/');
            var tenant = azureAd["TenantId"] ?? "common"; // /common accepts work/school + personal accounts

            auth.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Authority = $"{instance}/{tenant}/v2.0";
                options.ClientId = clientId;
                options.ClientSecret = azureAd["ClientSecret"];
                options.ResponseType = "code";
                options.UsePkce = true;
                options.CallbackPath = azureAd["CallbackPath"] ?? "/signin-oidc";
                options.SignedOutCallbackPath = azureAd["SignedOutCallbackPath"] ?? "/signout-callback-oidc";
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.SignInScheme = CookieScheme;
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                // Rule 4.3: shape-based issuer validation against an allowed tenant list.
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.IssuerValidator = (issuer, _, _) =>
                {
                    if (allowedTenants.Length == 0)
                        return issuer; // no restriction configured
                    if (allowedTenants.Any(t => issuer.Contains(t, StringComparison.OrdinalIgnoreCase)))
                        return issuer;
                    throw new SecurityTokenInvalidIssuerException(
                        $"Issuer '{issuer}' is not in the configured AllowedTenants list.");
                };
            });
        }

        // Dev/Test guest bypass: header-based FakeAuth scheme for automated tests (never in Production).
        if (flags.DeveloperBypassAuth)
        {
            if (builder.Environment.IsProduction())
                throw new InvalidOperationException(
                    "DeveloperBypassAuth (FakeAuth) must never be enabled in Production.");
            auth.AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>(FakeAuthHandler.SchemeName, null);
        }

        // Default authorization policy used by every .RequireAuthorization() call. In Production it
        // accepts only the BFF cookie (OIDC signs into the cookie). When the dev/test guest bypass is
        // enabled, the FakeAuth scheme is added so the header/guest handler can satisfy the policy —
        // this keeps the integration suite green without weakening the Production surface.
        // The default policy backs every explicit .RequireAuthorization() call. In Dev/Test it also accepts
        // the FakeAuth guest scheme so automated suites skip interactive login (rule 4.4 Test bypass).
        var defaultSchemes = flags.DeveloperBypassAuth
            ? new[] { CookieScheme, FakeAuthHandler.SchemeName }
            : new[] { CookieScheme };

        var defaultPolicy = new AuthorizationPolicyBuilder(defaultSchemes)
            .RequireAuthenticatedUser()
            .Build();

        // Rule 4.5 default-deny: the fallback policy protects any endpoint that never opts into a policy, so a
        // future slice that forgets .RequireAuthorization() is secure-by-default rather than silently anonymous.
        // It is deliberately COOKIE-ONLY — it must NOT include the always-succeeds FakeAuth scheme, or the
        // authorization middleware would auto-authenticate a guest on AllowAnonymous endpoints (e.g. /auth/me),
        // making them report an authenticated session before the user has actually signed in. The public surface
        // (SPA host page, static assets, /health, /diag, /auth sign-in routes) opts out via .AllowAnonymous().
        var fallbackPolicy = new AuthorizationPolicyBuilder(CookieScheme)
            .RequireAuthenticatedUser()
            .Build();

        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(defaultPolicy)
            .SetFallbackPolicy(fallbackPolicy);

        return oidcConfigured;
    }
}

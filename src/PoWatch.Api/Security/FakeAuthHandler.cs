using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace PoWatch.Api.Security;

/// <summary>
/// Development/Test-only authentication handler that maps the X-Fake-User and
/// X-Fake-Roles request headers to a <see cref="ClaimsPrincipal"/>.
/// Registered only when DeveloperBypassAuth is true — and never in Production
/// (registration throws in <c>Program</c> when the environment is Production).
/// </summary>
public sealed class FakeAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "FakeAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userName = Request.Headers["X-Fake-User"].FirstOrDefault() ?? "guest";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userName),
            new(ClaimTypes.Name, userName)
        };

        var roles = Request.Headers["X-Fake-Roles"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(roles))
            claims.AddRange(roles
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

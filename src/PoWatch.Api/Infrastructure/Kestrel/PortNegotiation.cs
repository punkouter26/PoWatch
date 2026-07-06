using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using PoWatch.Api.Observability;

namespace PoWatch.Api.Infrastructure.Kestrel;

/// <summary>
/// Self-healing port negotiator for Kestrel.
///
/// Why: the previous behaviour used static launchSettings.json ports (5000/5001). A stale
/// debug session holding the port produced <c>SocketException (10048)</c> during
/// <c>KestrelServer.StartAsync</c>, which ANCM surfaces to App Service as
/// <strong>HTTP 500.30 — ASP.NET Core app failed to start</strong>. The negotiator below
/// detects an in-use port, walks a small fallback list, and logs a structured
/// <see cref="HostStartupLog.PortConflictResolved"/> warning instead of failing the boot.
///
/// Behaviour matrix:
/// <list type="table">
///   <item><term>Development</term><description>Fall back automatically; warn.</description></item>
///   <item><term>Production (Azure App Service)</term><description>Bind exactly the
///     configured port or fail fast — port allocation outside the plan's mapped
///     ports would break the reverse proxy.</description></item>
/// </list>
/// </summary>
public static class PortNegotiation
{
    public static void Configure(KestrelServerOptions options, IConfiguration configuration, IHostEnvironment env, Serilog.ILogger logger)
    {
        // Honour a configured explicit endpoint first (Production App Service uses this).
        var kestrelSection = configuration.GetSection("Kestrel:Endpoints");
        if (kestrelSection.Exists())
        {
            // Bind exactly as configured — no negotiation in Production-style scenarios.
            ConfigureExplicitEndpoints(kestrelSection, options, logger);
            return;
        }

        // Default candidate list for development runs without explicit Kestrel config.
        var http = ReadPort(configuration, "PoWatch:Ports:Http", fallback: 5000);
        var https = ReadPort(configuration, "PoWatch:Ports:Https", fallback: 5001);

        // Already-picked ports are excluded from the fallback walk so that, when
        // both HTTP and HTTPS need to be reassigned, the negotiator never picks
        // the same free port for both (which would cause a self-collision).
        var taken = new HashSet<int>();

        var stopwatch = Stopwatch.StartNew();
        var actualHttp = NegotiatePort(http, env, logger, stopwatch, taken, isHttps: false);
        stopwatch.Stop();

        options.Listen(IPAddress.Loopback, actualHttp);
        taken.Add(actualHttp);

        int? actualHttps = null;
        // Only attempt HTTPS binding in development (matches the launchSettings "https" profile
        // and AGENT.MD rule T007 which suppresses UseHttpsRedirection in dev).
        if (env.IsDevelopment())
        {
            stopwatch.Restart();
            actualHttps = NegotiatePort(https, env, logger, stopwatch, taken, isHttps: true);
            stopwatch.Stop();
            options.Listen(IPAddress.Loopback, actualHttps.Value, lo => lo.UseHttps());
        }

        LogResolvedEndpoints(actualHttp, actualHttps, logger);
    }

    private static void ConfigureExplicitEndpoints(IConfigurationSection kestrel, KestrelServerOptions options, Serilog.ILogger logger)
    {
        // Production-style: bind whatever the operator declared. Failure here is intentional —
        // an App Service instance MUST bind the configured port.
        foreach (var endpoint in kestrel.GetChildren())
        {
            var url = endpoint["Url"];
            if (string.IsNullOrWhiteSpace(url)) continue;

            var uri = new Uri(url);
            var address = uri.HostNameType == UriHostNameType.IPv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
            options.Listen(address, uri.Port, lo =>
            {
                if (uri.Scheme == Uri.UriSchemeHttps)
                    lo.UseHttps();
            });
        }
        logger.Information("Kestrel bound to operator-configured endpoints: {Endpoints}",
            string.Join(", ", kestrel.GetChildren().Select(c => c["Url"]).Where(u => !string.IsNullOrWhiteSpace(u))));
    }

    private static int ReadPort(IConfiguration cfg, string key, int fallback)
    {
        var raw = cfg[key];
        return int.TryParse(raw, out var p) ? p : fallback;
    }

    /// <summary>
    /// Walk a 16-port range starting at the requested port and return the first
    /// one that is free (no listener on 127.0.0.1 or ::1, and not already taken
    /// by a sibling listener negotiated in the same host start). In Production
    /// we DO NOT fall back — failing fast here protects reverse-proxy mappings.
    /// </summary>
    private static int NegotiatePort(int requested, IHostEnvironment env, Serilog.ILogger logger, Stopwatch sw, HashSet<int> taken, bool isHttps)
    {
        var inUse = GetActiveLoopbackPorts();
        var combined = new HashSet<int>(inUse);
        combined.UnionWith(taken);

        if (!combined.Contains(requested))
            return requested;

        if (!env.IsDevelopment())
        {
            HostStartupLog.PortConflictFatal(logger, requested.ToString(), 1);
            throw new IOException(
                $"Kestrel could not bind to the configured {(isHttps ? "HTTPS" : "HTTP")} port {requested} " +
                $"because it is already in use. Refusing to fall back in {env.EnvironmentName}.");
        }

        for (var delta = 1; delta <= 16; delta++)
        {
            var candidate = requested + delta;
            if (!combined.Contains(candidate))
            {
                sw.Stop();
                HostStartupLog.PortConflictResolved(logger, requested, candidate, sw.ElapsedMilliseconds);
                sw.Restart();
                return candidate;
            }
        }

        HostStartupLog.PortConflictFatal(logger,
            string.Join(",", Enumerable.Range(requested, 16)),
            16);
        throw new IOException($"No free {(isHttps ? "HTTPS" : "HTTP")} port in range [{requested}..{requested + 16}].");
    }

    private static HashSet<int> GetActiveLoopbackPorts()
    {
        var ports = new HashSet<int>();
        var props = IPGlobalProperties.GetIPGlobalProperties();
        foreach (var listener in props.GetActiveTcpListeners())
        {
            if (IPAddress.IsLoopback(listener.Address))
                ports.Add(listener.Port);
        }
        return ports;
    }

    private static void LogResolvedEndpoints(int http, int? https, Serilog.ILogger logger)
    {
        if (https is null)
        {
            logger.Information("Kestrel HTTP listener bound to http://127.0.0.1:{Port}", http);
        }
        else
        {
            logger.Information(
                "Kestrel listeners bound: HTTP=http://127.0.0.1:{HttpPort}  HTTPS=https://127.0.0.1:{HttpsPort}",
                http, https);
        }
    }
}
using PoWatch.Api.Platform;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PoWatch.Api.Features.Diagnostics;
using PoWatch.Api.Features.Sessions;
using PoWatch.Api.Features.Ingest;
using PoWatch.Api.Features.Snapshots;
using PoWatch.Api.Features.Regulars;
using PoWatch.Api.Features.Recaps;
using PoWatch.Infrastructure.Runtime;
using PoWatch.Api.Features.Stats;
using PoWatch.Api.Features.Dev;
using PoWatch.Api.Features.Auth;
using PoWatch.Api.Features.Ask;
using PoWatch.Api.Features.Data;
using PoWatch.Api.HealthChecks;
using PoWatch.Api.Hosting;
using PoWatch.Api.Middleware;
using PoWatch.Api.Observability;
using PoWatch.Api.Security;
using PoWatch.Application;
using PoWatch.Application.Options;
using PoWatch.Infrastructure;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;

// Initialise Serilog early so startup errors are captured before host construction.
// Use a regular logger here so repeated WebApplicationFactory host creation in integration tests
// does not re-freeze a shared ReloadableLogger instance.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateLogger();

// Create the builder FIRST so we can install the Kestrel hooks on it.
var builder = WebApplication.CreateBuilder(args);

// Development only: step to the next free port when the configured one is held by another app.
builder.WebHost.ConfigureKestrel((ctx, opts) =>
    PortNegotiation.Configure(opts, ctx.Configuration, ctx.HostingEnvironment, Log.Logger.ForContext(typeof(PortNegotiation))));

// Two-stage Serilog initialisation — reads config from appsettings after host is built
builder.Host.UseSerilog(TelemetrySetup.ConfigureSerilog);

// Feature flags are not secrets, so they are read before Key Vault joins the configuration.
var featureFlags = builder.Configuration.GetSection("FeatureFlags").Get<FeatureFlagsOptions>() ?? new FeatureFlagsOptions();
if (featureFlags.EnableKeyVault && Uri.TryCreate(builder.Configuration["KeyVault:Uri"], UriKind.Absolute, out var kvUri))
    KeyVaultConfiguration.AddPoWatchKeyVault(builder.Configuration, kvUri, Log.Logger);

builder.Services.Configure<FeatureFlagsOptions>(builder.Configuration.GetSection("FeatureFlags"));
builder.Services.Configure<PoWatch.Application.Options.AiProviderOptions>(builder.Configuration.GetSection("AiProvider"));

// Fail-fast options — the startup-critical settings are validated and ValidateOnStart()
// forces evaluation during host build, so a bad table name, polling interval, or Azure OpenAI range
// aborts boot with an actionable message instead of throwing lazily on first use.
builder.Services.AddOptions<AzureStorageOptions>()
    .Bind(builder.Configuration.GetSection("AzureStorage"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<PoWatch.Application.Options.AzureOpenAiOptions>()
    .Bind(builder.Configuration.GetSection("AzureOpenAi"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Durable, shared Data Protection keyring so BFF auth cookies survive recycle/scale-out.
builder.AddPoWatchDataProtection();

// OpenAPI document at /openapi/v1.json, emitted at OpenAPI 3.1.
builder.Services.AddOpenApi(options =>
    options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_1);

// HybridCache for stats queries, tagged per user and evicted when a batch lands.
builder.Services.AddHybridCache(o =>
{
    o.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromSeconds(10),
        LocalCacheExpiration = TimeSpan.FromSeconds(10)
    };
});

// OpenTelemetry tracing (passes config so Azure Monitor exporter can be gated on connection string)
builder.Services.AddPoWatchTelemetry(builder.Configuration);

// Health checks — Azure Storage ping + Key Vault ping (when enabled) + JSON endpoint at /health
var hcBuilder = builder.Services.AddHealthChecks()
    .AddCheck<AzureStorageHealthCheck>("azure-storage");
if (featureFlags.EnableKeyVault)
    hcBuilder.AddCheck<KeyVaultHealthCheck>("azure-key-vault");

// Rate limiting on the API, sign-in and hub routes: 300 requests a minute per signed-in user (per IP
// when anonymous). A running session posts a batch every 10 s and polls stats, so 60 would trip.
// Static assets are exempt (a cold load is ~200 files); the Test host is exempt (suites share one guest).
builder.Services.AddRateLimiter(rl =>
{
    rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rl.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
    {
        var path = http.Request.Path;
        var limited = path.StartsWithSegments("/api") || path.StartsWithSegments("/auth") || path.StartsWithSegments("/hubs");
        if (!limited || builder.Environment.IsEnvironment("Test"))
            return RateLimitPartition.GetNoLimiter(string.Empty);

        var key = CurrentUser.Id(http.User) ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6
        });
    });
});

builder.Services.AddPoWatchApplication();
builder.Services.AddPoWatchInfrastructure();
// ponytail: in-memory recap response cache (200 MB cap), lost on restart; swap in Redis/SQL if that matters.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddPoWatchRecapAi(builder.Configuration);
builder.Services.AddSingleton<FluentValidation.IValidator<PoWatch.Shared.Models.IngestBatchDto>, IngestBatchValidator>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, CurrentUserIdProvider>();

// Global ProblemDetails middleware
builder.Services.AddProblemDetails();

// Auth: BFF cookie session + Microsoft Entra OIDC (when configured) + dev/test guest bypass.
builder.AddPoWatchAuthentication(featureFlags);

var app = builder.Build();

// OpenAPI + Scalar API reference UI
app.MapOpenApi("/openapi/v1.json");
app.MapScalarApiReference("/scalar/v1");

// Global exception handler — exposes detail only when ExposeDebugDetailsInUi is true
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalException");

        var flags = context.RequestServices.GetRequiredService<IOptions<FeatureFlagsOptions>>();
        var exceptionFeature = context.Features.Get<IExceptionHandlerPathFeature>();
        var exception = exceptionFeature?.Error;

        logger.LogError(
            exception,
            "Unhandled exception. Path={Path} TraceId={TraceId}",
            context.Request.Path,
            context.TraceIdentifier);

        var details = new ProblemDetails
        {
            Title = "An error occurred while processing the request.",
            Status = StatusCodes.Status500InternalServerError,
            Detail = flags.Value.ExposeDebugDetailsInUi
                ? exception?.ToString()
                : "An internal error occurred. See server logs for details.",
            Instance = context.Request.Path
        };
        details.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(details);
    });
});

// Only enforce HTTPS redirect in non-development environments.
// In dev the API binds only to HTTP; the middleware cannot resolve the HTTPS port and
// emits a WRN on every request otherwise.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseMiddleware<CorrelationIdMiddleware>();

// Auth middleware — always active (BFF cookie session + OIDC/guest schemes)
app.UseAuthentication();
app.UseAuthorization();
// After authentication: the limiter partitions by the signed-in user.
app.UseRateLimiter();

// Enrich every request log with UserId and SessionId from the current principal / trace identifier
app.Use(async (ctx, next) =>
{
    using (LogContext.PushProperty("UserId", ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous"))
    using (LogContext.PushProperty("SessionId", ctx.TraceIdentifier))
    {
        await next(ctx);
    }
});

// JSON health endpoint — returns status of each registered check
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            durationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = e.Value.Duration.TotalMilliseconds
            })
        });
    }
}).AllowAnonymous();

// UI-less diagnostics: masked environment keys + integration statuses, for signed-in callers.
app.MapGet("/diag", (PoWatch.Application.Contracts.IDiagnosticsProvider provider) =>
        Results.Ok(provider.CaptureSnapshot()))
    .WithName("Diag")
    .WithSummary("Masked environment keys and integration statuses.")
    .RequireAuthorization();

// Boot readiness: the fastest answer to "why did the app not come ready?". Reports per-dependency
// readiness WITHOUT secrets. Anonymous and dependency-light
// so it stays reachable even when a downstream dependency is degraded.
app.MapGet("/diag/boot", (
        PoWatch.Infrastructure.StartupReadiness readiness,
        IOptions<FeatureFlagsOptions> flags,
        IConfiguration config,
        IWebHostEnvironment env) =>
    {
        var storageConfigured = !string.IsNullOrWhiteSpace(config["AzureStorage:ConnectionString"])
                             || !string.IsNullOrWhiteSpace(config["AzureStorage:ServiceUri"]);
        var ready = readiness.StorageReady;
        var payload = new
        {
            environment = env.EnvironmentName,
            ready,
            storage = new
            {
                configured = storageConfigured,
                ready = readiness.StorageReady,
                detail = readiness.StorageDetail
            },
            keyVault = new
            {
                enabled = flags.Value.EnableKeyVault,
                uriConfigured = !string.IsNullOrWhiteSpace(config["KeyVault:Uri"])
            }
        };
        // 200 when ready, 503 when a dependency is not ready — so orchestrators/probes can act on it.
        return ready ? Results.Ok(payload) : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
    })
    .WithName("DiagBoot")
    .WithSummary("Per-dependency readiness (no secrets).")
    .AllowAnonymous();

// Serve hosted Blazor WASM from same origin — no CORS needed
// .NET 10: MapStaticAssets() replaces both UseBlazorFrameworkFiles() and UseStaticFiles().
// It uses the staticwebassets.endpoints.json manifest to resolve fingerprinted file names.
// App wwwroot/js files are NOT fingerprinted by the framework, so force revalidation on
// every request to prevent stale-cache errors after deployments.
app.Use(async (context, next) =>
{
    // Cross-origin isolation unlocks SharedArrayBuffer, so the vision model's WASM backend runs
    // multi-threaded on machines without WebGPU. "credentialless" (not require-corp) keeps the
    // Hugging Face weights and SAS snapshot images loading without CORP headers on their side.
    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
    context.Response.Headers["Cross-Origin-Embedder-Policy"] = "credentialless";

    if (context.Request.Path.StartsWithSegments("/js") ||
        context.Request.Path.StartsWithSegments("/css"))
    {
        context.Response.Headers["Cache-Control"] = "no-cache, must-revalidate";
    }
    await next();
});
// Static assets (WASM framework files, JS, CSS) must stay anonymous so the client can load and reach /login.
app.MapStaticAssets().AllowAnonymous();

// --- API routes ---
app.MapAuthEndpoints();
app.MapDiagnosticsFeature();
app.MapSessionsFeature();
app.MapIngestFeature();
app.MapSnapshotsFeature();
app.MapRegularsFeature();
app.MapRecapsFeature();
app.MapStatsFeature();
app.MapAskFeature();
app.MapDataFeature();
app.MapHub<StatsHub>(StatsHub.Path).RequireAuthorization();
app.MapDevSeedFeature(app.Environment);
// Uniform cross-app liveness probe (see PoPlatform). Same shape in every Po app, which
// is what lets the portfolio dashboard poll them all and render one uptime grid.
app.MapPoLiveness();

// Fall back to the Blazor WASM entry point for all unmatched requests. Anonymous: the SPA host page
// must load for unauthenticated users so the client can render /login (the fallback authz policy would
// otherwise 401 the host page itself and make the app unreachable).
app.MapFallbackToFile("index.html").AllowAnonymous();

// An unknown API route is a 404, not the SPA host page with a 200 (the more specific pattern wins).
app.MapFallback("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();

await app.RunAsync();

public partial class Program { }

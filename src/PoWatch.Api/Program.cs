using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PoWatch.Api.Features.Archives;
using PoWatch.Api.Features.Diagnostics;
using PoWatch.Api.Features.Fhir;
using PoWatch.Api.Features.Identity;
using PoWatch.Api.Features.Observer;
using PoWatch.Api.Endpoints;
using PoWatch.Api.HealthChecks;
using PoWatch.Api.Infrastructure.KeyVault;
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
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// T010: Two-stage Serilog initialisation — reads config from appsettings after host is built
builder.Host.UseSerilog(TelemetrySetup.ConfigureSerilog);

// Key Vault: add secrets as a config source before binding feature flags
var rawKvUri = builder.Configuration["KeyVault:Uri"] ?? builder.Configuration["KeyVaultUri"];
var tempFlags = builder.Configuration.GetSection("FeatureFlags").Get<FeatureFlagsOptions>() ?? new FeatureFlagsOptions();
if (tempFlags.EnableKeyVault && !string.IsNullOrWhiteSpace(rawKvUri) && Uri.TryCreate(rawKvUri, UriKind.Absolute, out var kvUri))
    KeyVaultConfiguration.AddPoWatchKeyVault(builder.Configuration, kvUri, Log.Logger);

// T013: Bind feature flags early so conditional registrations below can read them
var featureFlags = builder.Configuration
    .GetSection("FeatureFlags")
    .Get<FeatureFlagsOptions>() ?? new FeatureFlagsOptions();

builder.Services.Configure<FeatureFlagsOptions>(builder.Configuration.GetSection("FeatureFlags"));
builder.Services.Configure<AzureStorageOptions>(builder.Configuration.GetSection("AzureStorage"));
builder.Services.Configure<ObserverOptions>(builder.Configuration.GetSection("ObserverOptions"));
builder.Services.Configure<PoWatch.Application.Options.AlertThresholdOptions>(builder.Configuration.GetSection("AlertThresholds"));
builder.Services.Configure<PoWatch.Application.Options.DriftRadarOptions>(builder.Configuration.GetSection("DriftRadar"));
builder.Services.Configure<PoWatch.Application.Options.HandoffCoachOptions>(builder.Configuration.GetSection("HandoffCoach"));
builder.Services.Configure<PoWatch.Application.Options.AzureOpenAiOptions>(builder.Configuration.GetSection("AzureOpenAi"));

// T009: OpenAPI document at /openapi/v1.json
builder.Services.AddOpenApi();

// T010: OpenTelemetry tracing (passes config so Azure Monitor exporter can be gated on connection string)
builder.Services.AddPoWatchTelemetry(builder.Configuration);

// T009a: Health checks — Azure Storage ping + Key Vault ping (when enabled) + JSON endpoint at /health
var hcBuilder = builder.Services.AddHealthChecks()
    .AddCheck<AzureStorageHealthCheck>("azure-storage");
if (featureFlags.EnableKeyVault)
    hcBuilder.AddCheck<KeyVaultHealthCheck>("azure-key-vault");

// Rate limiting: 60 requests per minute per IP address (sliding window)
builder.Services.AddRateLimiter(rl =>
{
    rl.AddSlidingWindowLimiter("ApiPerIpPolicy", o =>
    {
        o.PermitLimit = 60;
        o.Window = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow = 6;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 5;
    });
    rl.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return ValueTask.CompletedTask;
    };
});

builder.Services.AddPoWatchApplication();
builder.Services.AddPoWatchInfrastructure();

// T012: Global ProblemDetails middleware
builder.Services.AddProblemDetails();

// T008: FakeAuth — development identity bypass (never registered when DeveloperBypassAuth is false)
if (featureFlags.DeveloperBypassAuth)
{
    builder.Services.AddAuthentication("FakeAuth")
        .AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>("FakeAuth", null);
    builder.Services.AddAuthorization();
}

var app = builder.Build();

// T009: OpenAPI + Scalar API reference UI
app.MapOpenApi("/openapi/v1.json");
app.MapScalarApiReference("/scalar/v1");

// T012: Global exception handler — exposes detail only when ExposeDebugDetailsInUi is true
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

// T007: Only enforce HTTPS redirect in non-development environments.
// In dev the API binds only to HTTP; the middleware cannot resolve the HTTPS port and
// emits a WRN on every request otherwise.
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseMiddleware<CorrelationIdMiddleware>();

// T008: Auth middleware — only active when FakeAuth is registered
if (featureFlags.DeveloperBypassAuth)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// Enrich every request log with UserId and SessionId from the current principal / trace identifier
app.Use(async (ctx, next) =>
{
    using (LogContext.PushProperty("UserId", ctx.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous"))
    using (LogContext.PushProperty("SessionId", ctx.TraceIdentifier))
    {
        await next(ctx);
    }
});

// T009a: JSON health endpoint — returns status of each registered check
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
});

// T005: Serve hosted Blazor WASM from same origin — no CORS needed (T006: CORS removed)
// .NET 10: MapStaticAssets() replaces both UseBlazorFrameworkFiles() and UseStaticFiles().
// It uses the staticwebassets.endpoints.json manifest to resolve fingerprinted file names.
// App wwwroot/js files are NOT fingerprinted by the framework, so force revalidation on
// every request to prevent stale-cache errors after deployments.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/js") ||
        context.Request.Path.StartsWithSegments("/css"))
    {
        context.Response.Headers["Cache-Control"] = "no-cache, must-revalidate";
    }
    await next();
});
app.MapStaticAssets();

// --- API routes ---
app.MapObserverFeature();
app.MapArchivesFeature();
app.MapIdentityFeature();
app.MapFhirFeature();
app.MapDiagnosticsFeature();

// T005: Fall back to the Blazor WASM entry point for all unmatched requests
app.MapFallbackToFile("index.html");

await app.RunAsync();

public partial class Program { }

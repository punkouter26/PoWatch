using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PoWatch.Api.Endpoints;
using PoWatch.Api.HealthChecks;
using PoWatch.Api.Observability;
using PoWatch.Api.Security;
using PoWatch.Application;
using PoWatch.Application.Options;
using PoWatch.Infrastructure;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

    // T010: Two-stage Serilog initialisation — reads config from appsettings after host is built
    builder.Host.UseSerilog(TelemetrySetup.ConfigureSerilog);

    // T013: Bind feature flags early so conditional registrations below can read them
    var featureFlags = builder.Configuration
        .GetSection("FeatureFlags")
        .Get<FeatureFlagsOptions>() ?? new FeatureFlagsOptions();

    builder.Services.Configure<FeatureFlagsOptions>(builder.Configuration.GetSection("FeatureFlags"));
    builder.Services.Configure<AzureStorageOptions>(builder.Configuration.GetSection("AzureStorage"));
    builder.Services.Configure<ObserverOptions>(builder.Configuration.GetSection("ObserverOptions"));

    // T009: OpenAPI document at /openapi/v1.json
    builder.Services.AddOpenApi();

    // T010: OpenTelemetry tracing (passes config so Azure Monitor exporter can be gated on connection string)
    builder.Services.AddPoWatchTelemetry(builder.Configuration);

    // T009a: Health checks — Azure Storage ping + JSON endpoint at /health
    builder.Services.AddHealthChecks()
        .AddCheck<AzureStorageHealthCheck>("azure-storage");

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

    app.UseHttpsRedirection();

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
    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();

    // --- API routes ---
    app.MapObserverEndpoints();
    app.MapArchivesEndpoints();
    app.MapBlobEndpoints();
    app.MapIdentityEndpoints();
    app.MapDiagnosticsEndpoints();

    // T005: Fall back to the Blazor WASM entry point for all unmatched requests
    app.MapFallbackToFile("index.html");

    await app.RunAsync();

public partial class Program { }
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PoWatch.Api.HealthChecks;
using PoWatch.Api.Observability;
using PoWatch.Api.Security;
using PoWatch.Application;
using PoWatch.Application.Models;
using PoWatch.Application.Options;
using PoWatch.Application.Services;
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

    var observer = app.MapGroup("/api/observer").WithTags("Observer");
    observer.MapPost("/ingest", async (
        IngestObservationRequest request,
        ObservationService service,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        logger.LogInformation(
            "Observer ingest API request received. Activity={Activity} SubjectHint={SubjectHint} TraceId={TraceId}",
            request.Activity,
            request.SubjectHint,
            Activity.Current?.TraceId.ToString());

        var result = await service.IngestAsync(request, cancellationToken);

        logger.LogInformation(
            "Observer ingest API request completed. Accepted={Accepted} Dropped={Dropped} SubjectId={SubjectId} TraceId={TraceId}",
            result.Accepted,
            result.Dropped,
            result.SubjectId,
            Activity.Current?.TraceId.ToString());

        return result.Dropped ? Results.Accepted(value: result) : Results.Ok(result);
    })
    .WithName("ObserverIngest")
    .WithSummary("Persist a locally inferred observation event.");

    observer.MapGet("/state", (ObservationService service) =>
        Results.Ok(service.GetRuntimeState()))
        .WithName("ObserverState")
        .WithSummary("Get the live observer runtime status and feature flags.");

    var archives = app.MapGroup("/api/archives").WithTags("Archives");
    archives.MapGet("/{date}", async (
        string date,
        ArchivesService service,
        CancellationToken cancellationToken) =>
    {
        if (!DateOnly.TryParse(date, out var parsedDate))
        {
            return Results.BadRequest(new { message = "Date must be in ISO format (yyyy-MM-dd)." });
        }

        var chapter = await service.GetChapterAsync(parsedDate, cancellationToken);
        return Results.Ok(chapter);
    })
    .WithName("ArchivesGetChapter")
    .WithSummary("Get the daily chapter narrative, timeline, and highlights for a date.");

    var blobs = app.MapGroup("/api/blobs").WithTags("Blobs");
    blobs.MapGet("/sas", async (
        string? subjectId,
        string? date,
        string? blobPath,
        BlobSasService service,
        CancellationToken cancellationToken) =>
    {
        if (!string.IsNullOrWhiteSpace(blobPath))
        {
            var readUrl = await service.CreateReadAccessUrlAsync(blobPath, cancellationToken);
            return Results.Ok(new { sasUrl = readUrl, blobPath, expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30) });
        }

        if (string.IsNullOrWhiteSpace(subjectId) || !DateOnly.TryParseExact(date, "yyyyMMdd", out var parsedDate))
        {
            return Results.BadRequest(new { message = "subjectId and date=yyyyMMdd are required." });
        }

        var access = await service.CreateUploadAccessAsync(subjectId, parsedDate, cancellationToken);
        return Results.Ok(access);
    })
    .WithName("BlobSasAccess")
    .WithSummary("Create time-limited read or upload access for evidence blobs.");

    var identity = app.MapGroup("/api/identity").WithTags("Identity");
    identity.MapGet("/subjects", async (
        IdentityService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetSubjectsAsync(cancellationToken)))
        .WithName("IdentitySubjects")
        .WithSummary("List all known and temporary subject identities.");

    identity.MapPatch("/subjects/{subjectId}", async (
        string subjectId,
        RenameSubjectRequest request,
        IdentityService service,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.NewName))
        {
            return Results.BadRequest(new { message = "newName is required." });
        }

        logger.LogInformation("Identity rename API request received. SubjectId={SubjectId} TraceId={TraceId}", subjectId, Activity.Current?.TraceId.ToString());
        var renamed = await service.RenameAsync(subjectId, request, cancellationToken);
        return Results.Ok(renamed);
    })
    .WithName("IdentityRename")
    .WithSummary("Rename a temporary subject and rewrite its historical identity references.");

    identity.MapPost("/merge", async (
        MergeIdentityRequest request,
        IdentityService service,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.PrimarySubjectId)
            || string.IsNullOrWhiteSpace(request.SecondarySubjectId))
        {
            return Results.BadRequest(new { message = "PrimarySubjectId and SecondarySubjectId are required." });
        }

        logger.LogInformation(
            "Identity merge API request received. PrimarySubjectId={PrimarySubjectId}, SecondarySubjectId={SecondarySubjectId}, TraceId={TraceId}",
            request.PrimarySubjectId,
            request.SecondarySubjectId,
            Activity.Current?.TraceId.ToString());

        var merged = await service.MergeAsync(request, cancellationToken);
        return Results.Ok(merged);
    })
    .WithName("IdentityMerge")
    .WithSummary("Merge two subject identities into one canonical history.");

    var diagnostics = app.MapGroup("/api/diagnostics").WithTags("Diagnostics");
    diagnostics.MapGet("/status", (DiagnosticsService service, ILogger<Program> logger) =>
    {
        logger.LogDebug("Diagnostics API request received. TraceId={TraceId}", Activity.Current?.TraceId.ToString());
        return Results.Ok(service.GetSnapshot());
    })
    .WithName("DiagnosticsStatus")
    .WithSummary("Get the masked system health snapshot for the current environment.");

    // T005: Fall back to the Blazor WASM entry point for all unmatched requests
    app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }


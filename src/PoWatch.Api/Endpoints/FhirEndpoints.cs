using System.Diagnostics;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Application.Services;

namespace PoWatch.Api.Endpoints;

internal static class FhirEndpoints
{
    internal static IEndpointRouteBuilder MapFhirEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/fhir").WithTags("FHIR")
            // Single feature-flag gate for all FHIR endpoints — no per-handler repetition
            .AddEndpointFilter(async (ctx, next) =>
            {
                var flags = ctx.HttpContext.RequestServices
                    .GetRequiredService<IOptions<FeatureFlagsOptions>>().Value;
                if (!flags.FhirExportEnabled)
                {
                    ctx.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("FhirEndpoints")
                        .LogInformation("FHIR request blocked — FhirExportEnabled is false.");
                    return Results.StatusCode(503);
                }
                return await next(ctx);
            });

        // GET /fhir/Observation?subject={id}&date={yyyy-MM-dd}[&_count={n}]
        group.MapGet("/Observation", async (
            string subject,
            string? date,
            int? count,
            IObservationRepository observationRepository,
            FhirMappingService fhirMapper,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(subject))
                return Results.BadRequest(new { message = "'subject' query parameter is required." });

            DateOnly targetDate;
            if (date is not null)
            {
                if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out targetDate))
                    return Results.BadRequest(new { message = "'date' must be in yyyy-MM-dd format." });
            }
            else
            {
                targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
            }

            var maxResults = Math.Clamp(count ?? 100, 1, 1000);

            logger.LogInformation(
                "FHIR Observation query. Subject={Subject} Date={Date} MaxResults={MaxResults} TraceId={TraceId}",
                subject, targetDate, maxResults, Activity.Current?.TraceId.ToString());

            var events = await observationRepository.GetByDateAsync(targetDate, cancellationToken);

            var filtered = events
                .Where(e => string.Equals(e.SubjectId, subject, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.ObservedAtUtc)
                .Take(maxResults)
                .ToList();

            var bundleId = Guid.NewGuid().ToString();
            var bundle = fhirMapper.MapToFhirBundle(filtered, bundleId);

            logger.LogInformation(
                "FHIR Observation bundle built. Subject={Subject} Date={Date} ResultCount={Count} BundleId={BundleId}",
                subject, targetDate, filtered.Count, bundleId);

            return Results.Json(bundle, contentType: "application/fhir+json");
        })
        .WithName("FhirObservationSearch")
        .WithSummary("FHIR R4 Observation search — returns a searchset Bundle for a subject on a given date.");

        // GET /fhir/Observation/{id} — single resource by event ID
        group.MapGet("/Observation/{id}", async (
            string id,
            IObservationRepository observationRepository,
            FhirMappingService fhirMapper,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(id))
                return Results.BadRequest(new { message = "id is required." });

            logger.LogInformation("FHIR single Observation requested. Id={Id} TraceId={TraceId}", id, Activity.Current?.TraceId.ToString());

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var events = await observationRepository.GetByDateAsync(today, cancellationToken);
            var obs = events.FirstOrDefault(e => e.Id.ToString().Equals(id, StringComparison.OrdinalIgnoreCase));

            if (obs is null)
            {
                logger.LogWarning("FHIR Observation not found. Id={Id}", id);
                return Results.NotFound(new { resourceType = "OperationOutcome", issue = new[] { new { severity = "error", code = "not-found", details = new { text = $"Observation '{id}' not found." } } } });
            }

            return Results.Json(fhirMapper.MapToFhirObservation(obs), contentType: "application/fhir+json");
        })
        .WithName("FhirObservationRead")
        .WithSummary("FHIR R4 Observation read by ID.");

        return app;
    }
}

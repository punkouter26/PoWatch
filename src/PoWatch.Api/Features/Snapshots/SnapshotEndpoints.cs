using PoWatch.Api.Security;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;
using PoWatch.Domain.Services;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Features.Snapshots;

internal static class SnapshotEndpoints
{
    private const int MaxMoments = 12;

    internal static IEndpointRouteBuilder MapSnapshotsFeature(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/snapshots", async (string? day, HttpContext http, ISnapshotStore store, CancellationToken ct) =>
            {
                if (CurrentUser.Id(http.User) is not { } userId) return Results.Unauthorized();
                if (!store.IsAvailable) return Results.Problem("No image storage is configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
                if (!DateOnly.TryParse(day, System.Globalization.CultureInfo.InvariantCulture, out var localDay))
                    return Results.BadRequest(new { message = "day must be yyyy-MM-dd (the browser's local date)." });

                var upload = await store.CreateUploadAsync(userId, localDay, ct);
                return upload is null
                    ? Results.Problem($"Today's snapshot cap ({ISnapshotStore.DailyCap}) is reached.", statusCode: StatusCodes.Status429TooManyRequests)
                    : Results.Ok(new SnapshotUploadDto { Path = upload.Path, UploadUrl = upload.UploadUrl.ToString() });
            })
            .WithTags("Snapshots")
            .RequireAuthorization()
            .WithName("CreateSnapshotUpload")
            .WithSummary("A short-lived, write-only link for one highlight snapshot under the caller's own prefix.");

        app.MapGet("/api/snapshots/read", async (string path, HttpContext http, ISnapshotStore store, CancellationToken ct) =>
            {
                if (CurrentUser.Id(http.User) is not { } userId) return Results.Unauthorized();
                var url = await store.CreateReadUrlAsync(userId, path, ct);
                return url is null ? Results.NotFound() : Results.Ok(new { url = url.ToString() });
            })
            .WithTags("Snapshots")
            .RequireAuthorization()
            .WithName("ReadSnapshot");

        app.MapGet("/api/sessions/{id:guid}/moments", async (
                Guid id,
                HttpContext http,
                ISessionRepository sessions,
                ISensingLog log,
                ISnapshotStore store,
                TimeProvider time,
                CancellationToken ct) =>
            {
                if (CurrentUser.Id(http.User) is not { } userId) return Results.Unauthorized();
                if (await sessions.GetAsync(userId, id, ct) is not { } session) return Results.NotFound();

                var zone = session.TimeZone;
                var end = session.EndedUtc ?? time.GetUtcNow();
                var notable = new List<SceneEvent>();
                for (var day = LocalDay.Of(session.StartedUtc, zone); day <= LocalDay.Of(end, zone); day = day.AddDays(1))
                    notable.AddRange((await log.GetEventsAsync(userId, day, ct)).Where(e => e.SessionId == id && e.Kind == SceneEventKind.Notable));

                var moments = new List<MomentDto>();
                foreach (var moment in notable.OrderByDescending(e => e.Score ?? 0).ThenBy(e => e.AtUtc).Take(MaxMoments))
                {
                    var url = moment.ImagePath is { } path ? await store.CreateReadUrlAsync(userId, path, ct) : null;
                    moments.Add(new MomentDto { AtUtc = moment.AtUtc, Text = moment.Text ?? string.Empty, Score = moment.Score ?? 0, ImageUrl = url?.ToString() });
                }

                return Results.Ok(moments);
            })
            .WithTags("Sessions")
            .RequireAuthorization()
            .WithName("SessionMoments")
            .WithSummary("A session's notable moments, best first, with snapshot links where one was kept.");

        return app;
    }
}

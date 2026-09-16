using System.Diagnostics;
using PoWatch.Application.Contracts;
using PoWatch.Application.Services;
using PoWatch.Domain.Models;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Features.Handoff.Memos;

internal static class HandoffMemoEndpoints
{
    internal static IEndpointRouteBuilder MapHandoffMemoFeature(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/handoff/memos").WithTags("Handoff Memos").RequireAuthorization();

        // Upload. The browser posts a multipart/form-data with the audio bytes in `file` and
        // metadata fields alongside (subjectId, durationMs). Default multipart limits in ASP.NET
        // Core comfortably accommodate a 30-second Opus memo (~120 KB); if a future feature
        // needs longer recordings, raise <c>FormOptions.MultipartBodyLengthLimit</c> in Program.cs.
        group.MapPost("", async (
            HttpContext httpContext,
            HandoffMemoService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var form = await httpContext.Request.ReadFormAsync(cancellationToken);
            var file = form.Files["file"];
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "Audio file is required (multipart field 'file')." });
            }

            SubjectId? subjectId = null;
            if (form.TryGetValue("subjectId", out var subjectRaw) && !string.IsNullOrWhiteSpace(subjectRaw))
            {
                subjectId = SubjectId.From(subjectRaw.ToString());
            }

            int durationMs = 0;
            if (form.TryGetValue("durationMs", out var durationRaw) && int.TryParse(durationRaw, out var parsedDuration))
            {
                durationMs = parsedDuration;
            }

            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "audio/webm" : file.ContentType;
            var memoId = Guid.NewGuid().ToString("N");

            byte[] bytes;
            await using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, cancellationToken);
                bytes = ms.ToArray();
            }

            var actor = ResolveActor(httpContext);
            var memo = await service.RecordAsync(memoId, subjectId, contentType, durationMs, bytes, actor, cancellationToken);

            logger.LogInformation(
                "Handoff memo uploaded. MemoId={MemoId} SubjectId={SubjectId} DurationMs={DurationMs} SizeBytes={SizeBytes} ContentType={ContentType}",
                memo.Id, memo.SubjectId, memo.DurationMs, bytes.Length, memo.ContentType);

            return Results.Ok(new HandoffMemoDto
            {
                Id = memo.Id,
                SubjectId = memo.SubjectId?.Value,
                DurationMs = memo.DurationMs,
                ContentType = memo.ContentType,
                CreatedAtUtc = memo.CreatedAtUtc,
                AuthorUserId = memo.AuthorUserId
            });
        })
        .WithName("HandoffMemoUpload")
        .WithSummary("Upload a voice memo attached to a shift handoff (multipart/form-data).")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<HandoffMemoDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .DisableAntiforgery();

        group.MapGet("", async (
            int? limit,
            HandoffMemoService service,
            CancellationToken cancellationToken) =>
        {
            var effectiveLimit = Math.Clamp(limit ?? 20, 1, 100);
            var memos = await service.ListRecentAsync(effectiveLimit, cancellationToken);
            return Results.Ok(memos.Select(ToDto).ToList());
        })
        .WithName("HandoffMemoListRecent")
        .WithSummary("List recent voice memos (default 20, max 100).")
        .Produces<List<HandoffMemoDto>>(StatusCodes.Status200OK);

        group.MapGet("{id}/audio", async (
            string id,
            HandoffMemoService service,
            IHandoffMemoStore store,
            CancellationToken cancellationToken) =>
        {
            var memo = await service.GetAsync(id, cancellationToken);
            if (memo is null) return Results.NotFound();

            var blob = await store.DownloadAsync(memo.BlobPath, cancellationToken);
            if (blob is null) return Results.NotFound();

            return Results.File(blob.Value.Bytes, blob.Value.ContentType);
        })
        .WithName("HandoffMemoDownload")
        .WithSummary("Stream the audio bytes of a memo. No transcription; returns the original codec.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("{id}", async (
            string id,
            HttpContext httpContext,
            HandoffMemoService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            var actor = ResolveActor(httpContext);
            var deleted = await service.DeleteAsync(id, actor, cancellationToken);
            if (!deleted)
            {
                return Results.NotFound();
            }
            logger.LogInformation("Handoff memo deleted via API. MemoId={MemoId} Actor={Actor}", id, actor);
            return Results.NoContent();
        })
        .WithName("HandoffMemoDelete")
        .WithSummary("Delete a voice memo. Idempotent — a second delete returns 404.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static HandoffMemoDto ToDto(HandoffMemo memo) => new()
    {
        Id = memo.Id,
        SubjectId = memo.SubjectId?.Value,
        DurationMs = memo.DurationMs,
        ContentType = memo.ContentType,
        CreatedAtUtc = memo.CreatedAtUtc,
        AuthorUserId = memo.AuthorUserId
    };

    private static string ResolveActor(HttpContext httpContext) =>
        httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? httpContext.User?.FindFirst("sub")?.Value
        ?? "anonymous";
}

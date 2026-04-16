using PoWatch.Application.Contracts;
using PoWatch.Shared.Models;

namespace PoWatch.Api.Endpoints;

internal static class BlobEndpoints
{
    internal static IEndpointRouteBuilder MapBlobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/blobs").WithTags("Blobs");

        group.MapGet("/sas", async (
            string? subjectId,
            string? date,
            string? blobPath,
            bool? upload,
            IBlobSasProvider provider,
            CancellationToken cancellationToken) =>
        {
            if (!string.IsNullOrWhiteSpace(blobPath))
            {
                if (upload == true)
                {
                    var uploadAccess = await provider.CreateUploadAccessForBlobAsync(blobPath, cancellationToken);
                    return Results.Ok(uploadAccess);
                }

                var readUrl = await provider.CreateReadAccessUrlAsync(blobPath, cancellationToken);
                return Results.Ok(new BlobAccessDescriptorDto
                {
                    SasUrl = readUrl,
                    BlobPath = blobPath,
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
                });
            }

            if (string.IsNullOrWhiteSpace(subjectId) || !DateOnly.TryParseExact(date, "yyyyMMdd", out var parsedDate))
            {
                return Results.BadRequest(new { message = "subjectId and date=yyyyMMdd are required when blobPath is omitted." });
            }

            var access = await provider.CreateUploadAccessAsync(subjectId, parsedDate, cancellationToken);
            return Results.Ok(access);
        })
        .WithName("BlobSasAccess")
        .WithSummary("Create time-limited read or upload access for evidence blobs.");

        return app;
    }
}

using PoWatch.Application.Contracts;
using PoWatch.Shared.Models;
using System.Collections.Generic;

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

        // Direct read endpoint (returns signed read URL immediately)
        group.MapGet("/read", async (
            string? blobPath,
            IBlobSasProvider provider,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(blobPath))
                return Results.BadRequest(new { message = "blobPath is required." });

            var readUrl = await provider.CreateReadAccessUrlAsync(blobPath, cancellationToken);
            return Results.Ok(new BlobAccessDescriptorDto
            {
                SasUrl = readUrl,
                BlobPath = blobPath,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            });
        })
        .WithName("BlobReadAccess")
        .WithSummary("Get a signed read URL for a blob (for image retrieval).");

        // Evidence integrity check endpoint (checks multiple blobs and returns status)
        group.MapPost("/integrity", async (
            string[] blobPaths,
            IBlobSasProvider provider,
            CancellationToken cancellationToken) =>
        {
            if (blobPaths?.Length == 0)
                return Results.BadRequest(new { message = "At least one blobPath is required." });

            var results = new List<BlobIntegrityCheckDto>();

            foreach (var blobPath in blobPaths ?? [])
            {
                try
                {
                    var readUrl = await provider.CreateReadAccessUrlAsync(blobPath, cancellationToken);
                    var hasSasToken = readUrl.Contains("?") && readUrl.Contains("sig=");
                    
                    results.Add(new BlobIntegrityCheckDto
                    {
                        BlobPath = blobPath,
                        Status = hasSasToken ? "HasValidSas" : "NoSasToken",
                        ReadUrl = readUrl,
                        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30),
                        IsViewable = hasSasToken
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new BlobIntegrityCheckDto
                    {
                        BlobPath = blobPath,
                        Status = "Error",
                        ErrorMessage = ex.Message,
                        IsViewable = false
                    });
                }
            }

            return Results.Ok(new { totalChecked = results.Count, results });
        })
        .WithName("BlobIntegrityCheck")
        .WithSummary("Check integrity and viewability of multiple evidence blobs (read-access validation).");

        return app;
    }
}

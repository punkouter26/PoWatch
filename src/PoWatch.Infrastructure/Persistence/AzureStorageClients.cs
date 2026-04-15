using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using PoWatch.Application.Options;

namespace PoWatch.Infrastructure.Persistence;

/// <summary>
/// Provides pre-configured Azure Storage service clients for table and blob operations.
/// Registered as a singleton; a single instance is shared for the process lifetime.
/// </summary>
public sealed class AzureStorageClients
{
    private readonly string? _connectionString;

    public TableServiceClient TableService { get; }
    public BlobServiceClient BlobService { get; }

    public AzureStorageClients(IOptions<AzureStorageOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
        TableService = new TableServiceClient(_connectionString);
        BlobService = new BlobServiceClient(_connectionString);

        ConfigureDevelopmentCorsIfNeeded(_connectionString, BlobService);
    }

    public void EnsureDevelopmentBlobCorsConfigured()
    {
        ConfigureDevelopmentCorsIfNeeded(_connectionString, BlobService);
    }

    private static void ConfigureDevelopmentCorsIfNeeded(string? connectionString, BlobServiceClient blobService)
    {
        if (!string.Equals(connectionString, "UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var allowedOrigins = new[]
        {
            "http://localhost:5000",
            "https://localhost:5001",
            "http://127.0.0.1:5000",
            "https://127.0.0.1:5001"
        };

        try
        {
            var properties = blobService.GetProperties().Value;
            var hasMatchingRule = properties.Cors.Any(rule =>
                allowedOrigins.All(origin => rule.AllowedOrigins?.Contains(origin, StringComparison.OrdinalIgnoreCase) == true)
                && string.Equals(rule.AllowedMethods, "GET,PUT,HEAD,OPTIONS", StringComparison.OrdinalIgnoreCase));

            if (hasMatchingRule)
            {
                return;
            }

            properties.Cors.Add(new BlobCorsRule
            {
                AllowedOrigins = string.Join(",", allowedOrigins),
                AllowedMethods = "GET,PUT,HEAD,OPTIONS",
                AllowedHeaders = "*",
                ExposedHeaders = "*",
                MaxAgeInSeconds = 3600
            });

            blobService.SetProperties(properties);
        }
        catch
        {
            // Keep local development running even when Azurite is not available.
        }
    }
}

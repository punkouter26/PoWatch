using Azure.Data.Tables;
using Azure.Identity;
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
    // Lazy ensures CORS setup runs at most once per singleton lifetime (thread-safe by default).
    // Deferred from the constructor to avoid synchronous blocking HTTP on the DI construction thread.
    private readonly Lazy<bool> _devCorsConfigured;

    public TableServiceClient TableService { get; }
    public BlobServiceClient BlobService { get; }

    public AzureStorageClients(IOptions<AzureStorageOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
        var serviceUri = options.Value.ServiceUri;

        // Prefer Managed Identity when a ServiceUri is configured; fall back to connection string.
        if (!string.IsNullOrWhiteSpace(serviceUri) &&
            Uri.TryCreate(serviceUri, UriKind.Absolute, out var tableUri))
        {
            var credential = new DefaultAzureCredential();
            TableService = new TableServiceClient(tableUri, credential);
            // Blob endpoint is derived from the table URI host (same storage account).
            var blobUri = new Uri($"https://{tableUri.Host.Replace(".table.", ".blob.")}");
            BlobService = new BlobServiceClient(blobUri, credential);
        }
        else
        {
            TableService = new TableServiceClient(_connectionString);
            BlobService = new BlobServiceClient(_connectionString);
        }

        var allowedOrigins = options.Value.DevCorsAllowedOrigins;
        _devCorsConfigured = new Lazy<bool>(() =>
        {
            ConfigureDevelopmentCorsIfNeeded(_connectionString, BlobService, allowedOrigins);
            return true;
        });
    }

    public void EnsureDevelopmentBlobCorsConfigured()
    {
        // Thread-safe; ConfigureDevelopmentCorsIfNeeded runs exactly once per lifetime.
        _ = _devCorsConfigured.Value;
    }

    private static void ConfigureDevelopmentCorsIfNeeded(string? connectionString, BlobServiceClient blobService, string[] allowedOrigins)
    {
        if (!string.Equals(connectionString, "UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (allowedOrigins.Length == 0)
        {
            return;
        }

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

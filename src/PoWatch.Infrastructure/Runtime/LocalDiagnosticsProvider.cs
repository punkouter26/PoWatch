using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;

namespace PoWatch.Infrastructure.Runtime;

public sealed class LocalDiagnosticsProvider(
    IOptions<AzureStorageOptions> storageOptions,
    ILogger<LocalDiagnosticsProvider> logger) : IDiagnosticsProvider
{
    private (TimeSpan CpuTime, DateTime WallTime) _lastCpuSample;

    public DiagnosticsSnapshot CaptureSnapshot()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var memoryMb = Math.Round(process.WorkingSet64 / 1024d / 1024d, 2);

            var nowUtc = DateTime.UtcNow;
            var currentCpuTime = process.TotalProcessorTime;
            double cpuEstimate;
            if (_lastCpuSample.WallTime == default || (nowUtc - _lastCpuSample.WallTime).TotalSeconds < 0.5)
            {
                cpuEstimate = 0;
            }
            else
            {
                var wallElapsed = (nowUtc - _lastCpuSample.WallTime).TotalSeconds;
                var cpuElapsed = (currentCpuTime - _lastCpuSample.CpuTime).TotalSeconds;
                cpuEstimate = Math.Round(Math.Min(100, cpuElapsed / (Environment.ProcessorCount * wallElapsed) * 100), 1);
            }
            _lastCpuSample = (currentCpuTime, nowUtc);

            var connectionString = storageOptions.Value.ConnectionString ?? string.Empty;
            var isAzurite = IsAzuriteConnectionString(connectionString);
            var endpoint = isAzurite
                ? GetConnectionValue(connectionString, "BlobEndpoint") ?? "http://127.0.0.1:10000/devstoreaccount1"
                : GetConnectionValue(connectionString, "BlobEndpoint") ?? "managed-identity-vault";
            var apiKey = isAzurite
                ? GetConnectionValue(connectionString, "AccountKey") ?? "DEV-LOCAL-KEY-12345"
                : GetConnectionValue(connectionString, "AccountKey") ?? "managed-identity";

            logger.LogDebug(
                "Diagnostics storage classification evaluated. IsAzurite={IsAzurite} EndpointHost={EndpointHost}",
                isAzurite,
                endpoint);

            var snapshot = new DiagnosticsSnapshot
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                CpuLoadPercent = cpuEstimate,
                MemoryMb = memoryMb,
                StorageConnectionStatus = isAzurite ? "Azurite-OK" : "Azure-Configured",
                MaskedEndpoint = MaskingUtility.MaskMiddle(endpoint),
                MaskedApiKey = MaskingUtility.MaskMiddle(apiKey)
            };

            logger.LogDebug(
                "Diagnostics snapshot captured. StorageConnectionStatus={StorageConnectionStatus} CpuLoadPercent={CpuLoadPercent} MemoryMb={MemoryMb}",
                snapshot.StorageConnectionStatus,
                snapshot.CpuLoadPercent,
                snapshot.MemoryMb);

            return snapshot;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Diagnostics snapshot capture degraded; returning safe fallback values.");
            return new DiagnosticsSnapshot
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                CpuLoadPercent = 0,
                MemoryMb = 0,
                StorageConnectionStatus = "Unavailable",
                MaskedEndpoint = "***",
                MaskedApiKey = "***"
            };
        }
    }

    private static bool IsAzuriteConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        if (string.Equals(connectionString, "UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var accountName = GetConnectionValue(connectionString, "AccountName");
        var blobEndpoint = GetConnectionValue(connectionString, "BlobEndpoint");
        var tableEndpoint = GetConnectionValue(connectionString, "TableEndpoint");

        return string.Equals(accountName, "devstoreaccount1", StringComparison.OrdinalIgnoreCase)
            || IsLocalAzuriteEndpoint(blobEndpoint)
            || IsLocalAzuriteEndpoint(tableEndpoint);
    }

    private static bool IsLocalAzuriteEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        return endpoint.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("azurite", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("devstoreaccount1", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetConnectionValue(string connectionString, string key)
    {
        return connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .FirstOrDefault(parts => string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase))?[1];
    }
}

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Domain.Models;

namespace PoWatch.Infrastructure.Runtime;

public sealed class LocalDiagnosticsProvider(
    IOptions<AzureStorageOptions> storageOptions,
    ILogger<LocalDiagnosticsProvider>? logger = null) : IDiagnosticsProvider
{
    public DiagnosticsSnapshot CaptureSnapshot()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var memoryMb = Math.Round(process.WorkingSet64 / 1024d / 1024d, 2);
            var cpuEstimate = Math.Min(100, Math.Max(1, Environment.ProcessorCount * 6));

            var connectionString = storageOptions.Value.ConnectionString ?? string.Empty;
            var isAzurite = string.Equals(connectionString, "UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase);
            var endpoint = isAzurite
                ? "http://127.0.0.1:10000/devstoreaccount1"
                : GetConnectionValue(connectionString, "BlobEndpoint") ?? "managed-identity-vault";
            var apiKey = isAzurite
                ? "DEV-LOCAL-KEY-12345"
                : GetConnectionValue(connectionString, "AccountKey") ?? "managed-identity";

            var snapshot = new DiagnosticsSnapshot
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                CpuLoadPercent = cpuEstimate,
                MemoryMb = memoryMb,
                StorageConnectionStatus = isAzurite ? "Azurite-OK" : "Azure-Configured",
                MaskedEndpoint = MaskingUtility.MaskMiddle(endpoint),
                MaskedApiKey = MaskingUtility.MaskMiddle(apiKey)
            };

            (logger ?? NullLogger<LocalDiagnosticsProvider>.Instance).LogInformation(
                "Diagnostics snapshot captured. StorageConnectionStatus={StorageConnectionStatus} CpuLoadPercent={CpuLoadPercent} MemoryMb={MemoryMb}",
                snapshot.StorageConnectionStatus,
                snapshot.CpuLoadPercent,
                snapshot.MemoryMb);

            return snapshot;
        }
        catch (Exception ex)
        {
            (logger ?? NullLogger<LocalDiagnosticsProvider>.Instance).LogWarning(ex, "Diagnostics snapshot capture degraded; returning safe fallback values.");

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

    private static string? GetConnectionValue(string connectionString, string key)
    {
        return connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .FirstOrDefault(parts => string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase))?[1];
    }
}

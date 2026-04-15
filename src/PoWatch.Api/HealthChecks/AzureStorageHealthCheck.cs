using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Azure.Data.Tables;
using PoWatch.Application.Options;

namespace PoWatch.Api.HealthChecks;

// Pings Azure Table Storage service properties to verify the connection is reachable.
// Returns Degraded (not Unhealthy) when storage is intentionally absent (in-memory mode).
public sealed class AzureStorageHealthCheck(IOptions<AzureStorageOptions> storageOptions) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = storageOptions.Value.ConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
            return HealthCheckResult.Degraded("Azure Storage is not configured; running in in-memory mode.");

        try
        {
            var client = new TableServiceClient(connectionString);
            await client.GetPropertiesAsync(cancellationToken);
            return HealthCheckResult.Healthy("Azure Table Storage is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Azure Table Storage is unreachable.", ex);
        }
    }
}

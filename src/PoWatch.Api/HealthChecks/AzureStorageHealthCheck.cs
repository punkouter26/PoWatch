using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Azure.Data.Tables;
using Azure.Identity;
using PoWatch.Application.Options;

namespace PoWatch.Api.HealthChecks;

// Pings Azure Table Storage service properties to verify the connection is reachable.
// Supports both connection-string auth and Managed Identity (ServiceUri).
// Returns Degraded (not Unhealthy) when storage is intentionally absent (in-memory mode).
public sealed class AzureStorageHealthCheck(IOptions<AzureStorageOptions> storageOptions) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var opts = storageOptions.Value;

        TableServiceClient client;
        if (!string.IsNullOrWhiteSpace(opts.ConnectionString))
        {
            client = new TableServiceClient(opts.ConnectionString);
        }
        else if (!string.IsNullOrWhiteSpace(opts.ServiceUri) &&
                 Uri.TryCreate(opts.ServiceUri, UriKind.Absolute, out var serviceUri))
        {
            client = new TableServiceClient(serviceUri, new DefaultAzureCredential());
        }
        else
        {
            return HealthCheckResult.Degraded("Azure Storage is not configured; running in in-memory mode.");
        }

        try
        {
            await client.GetPropertiesAsync(cancellationToken);
            return HealthCheckResult.Healthy("Azure Table Storage is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Azure Table Storage is unreachable.", ex);
        }
    }
}

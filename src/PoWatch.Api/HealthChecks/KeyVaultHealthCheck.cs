using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PoWatch.Api.HealthChecks;

/// <summary>
/// Health check that verifies Azure Key Vault connectivity using Managed Identity.
/// Skipped gracefully when KeyVaultUri is not configured (local dev without KV).
/// </summary>
public sealed class KeyVaultHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeyVaultHealthCheck> _logger;

    public KeyVaultHealthCheck(IConfiguration configuration, ILogger<KeyVaultHealthCheck> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var rawUri = _configuration["KeyVaultUri"];
        if (string.IsNullOrWhiteSpace(rawUri))
            return HealthCheckResult.Healthy("Key Vault not configured — check skipped");

        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var vaultUri))
            return HealthCheckResult.Degraded($"KeyVaultUri is not a valid URI: '{rawUri}'");

        _logger.LogDebug("Running Key Vault health check. Uri={VaultUri}", vaultUri);

        try
        {
            var credential = new Azure.Identity.DefaultAzureCredential();
            var client = new SecretClient(vaultUri, credential);

            // List first page of secret names — lightweight connectivity probe
            await foreach (var _ in client.GetPropertiesOfSecretsAsync(cancellationToken)
                               .AsPages(pageSizeHint: 1)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                break;
            }

            _logger.LogDebug("Key Vault health check passed. Uri={VaultUri}", vaultUri);
            return HealthCheckResult.Healthy($"Azure Key Vault is reachable. Uri={vaultUri}");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Key Vault health check failed. Uri={VaultUri} Status={StatusCode}", vaultUri, ex.Status);
            return HealthCheckResult.Degraded($"Azure Key Vault is unreachable (HTTP {ex.Status}). Uri={vaultUri}", ex);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Key Vault health check timed out or was cancelled");
            return HealthCheckResult.Degraded("Key Vault health check cancelled", ex);
        }
        catch (AggregateException ex)
        {
            _logger.LogWarning(ex, "Key Vault health check failed with wrapped network error. Uri={VaultUri}", vaultUri);
            return HealthCheckResult.Degraded("Key Vault health check failed due to network error", ex);
        }
    }
}

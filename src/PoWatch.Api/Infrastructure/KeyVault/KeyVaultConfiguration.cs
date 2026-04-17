using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using ILogger = Serilog.ILogger;

namespace PoWatch.Api.Infrastructure.KeyVault;

/// <summary>
/// Loads secrets from Azure Key Vault using Managed Identity.
/// All secrets are resolved at startup so no plaintext credentials exist in config files.
/// </summary>
public sealed class KeyVaultConfiguration
{
    /// <summary>
    /// Adds Azure Key Vault as a configuration source using DefaultAzureCredential.
    /// </summary>
    public static IConfigurationBuilder AddPoWatchKeyVault(
        IConfigurationBuilder builder,
        Uri keyVaultUri,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(keyVaultUri);
        ArgumentNullException.ThrowIfNull(logger);

        logger.Information("Adding Azure Key Vault configuration source. Uri={KeyVaultUri}", keyVaultUri);

        try
        {
            var credential = new DefaultAzureCredential();
            builder.AddAzureKeyVault(keyVaultUri, credential);
            logger.Information("Key Vault configuration source added successfully");
        }
        catch (Azure.RequestFailedException ex)
        {
            // Log and continue — app will fail later if a required secret is missing.
            logger.Warning(ex,
                "Failed to load Key Vault secrets (Azure API error). Uri={KeyVaultUri} Status={StatusCode}",
                keyVaultUri, ex.Status);
        }
        catch (AggregateException ex)
        {
            logger.Warning(ex,
                "Failed to load Key Vault secrets (network error). Uri={KeyVaultUri} — running without KV secrets.",
                keyVaultUri);
        }
        catch (UriFormatException ex)
        {
            logger.Error(ex, "Invalid Key Vault URI format. Uri={KeyVaultUri}", keyVaultUri);
        }

        return builder;
    }
}

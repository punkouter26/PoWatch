using Azure.Core;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
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
            // Transient-fault resilience: a bounded exponential retry rides out a KV/network blip at boot
            // instead of the previous no-retry, fail-silent behaviour that let the app start with secrets
            // quietly absent. A genuine, sustained failure now propagates so startup fails fast and loud.
            var clientOptions = new SecretClientOptions();
            clientOptions.Retry.MaxRetries = 5;
            clientOptions.Retry.Mode = RetryMode.Exponential;
            clientOptions.Retry.Delay = TimeSpan.FromMilliseconds(500);
            clientOptions.Retry.MaxDelay = TimeSpan.FromSeconds(10);

            var credential = new DefaultAzureCredential();
            var secretClient = new SecretClient(keyVaultUri, credential, clientOptions);
            builder.AddAzureKeyVault(secretClient, new KeyVaultSecretManager());
            logger.Information("Key Vault configuration source added successfully");
        }
        catch (UriFormatException ex)
        {
            logger.Error(ex, "Invalid Key Vault URI format. Uri={KeyVaultUri}", keyVaultUri);
            throw;
        }
        catch (Exception ex)
        {
            // Fail fast: Key Vault was explicitly enabled and configured, so booting without its secrets
            // is a misconfiguration, not a soft-degrade. Surface it rather than 500-ing every later request.
            logger.Fatal(ex,
                "Key Vault secrets could not be loaded after retries. Uri={KeyVaultUri} — aborting startup.",
                keyVaultUri);
            throw;
        }

        return builder;
    }
}

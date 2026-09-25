using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Options;
using PoWatch.Infrastructure.Persistence;

namespace PoWatch.Infrastructure;

/// <summary>
/// One-shot hosted service that ensures all required Azure Storage tables and blob containers
/// exist before the application begins serving requests.
/// </summary>
public sealed class AzureStorageInitializer(
    AzureStorageClients clients,
    IOptions<AzureStorageOptions> options,
    IHostEnvironment environment,
    StartupReadiness readiness,
    ILogger<AzureStorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing Azure Storage tables and containers...");

        try
        {
            var tableService = clients.TableService;

            foreach (var table in new[] { options.Value.SessionsTable, options.Value.TicksTable, options.Value.SceneEventsTable, options.Value.IngestLedgerTable, options.Value.RollupsTable, options.Value.AchievementsTable, options.Value.RegularsTable })
                await tableService.GetTableClient(table).CreateIfNotExistsAsync(cancellationToken);

            await clients.BlobService
                .GetBlobContainerClient(options.Value.SnapshotsContainer)
                .CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            // Container for the persisted Data Protection keyring (BFF cookie encryption keys). The blob
            // provider creates the key blob on demand but never the container, so ensure it exists here.
            await clients.BlobService
                .GetBlobContainerClient(options.Value.DataProtectionKeysContainer)
                .CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            logger.LogInformation("Azure Storage initialization complete.");
            readiness.MarkStorageReady("Azure Storage reachable; tables and containers ready.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "Azure Storage initialization failed; reads/writes will fail until the dependency recovers. " +
                "Verify Azurite/Docker is running, or that the Managed Identity holds Storage Table/Blob Data Contributor. " +
                "ServiceUri={ServiceUri} ErrorType={ErrorType} Detail={Detail}",
                options.Value.ServiceUri,
                ex.GetType().Name,
                ex.Message);

            readiness.MarkStorageFailed($"Storage init failed: {ex.GetType().Name} — {ex.Message}");

            // In Development, fail fast and loud so a local misconfiguration (Azurite not running) is
            // impossible to miss. In hosted environments, DO NOT abort host construction: a throw here
            // surfaces on App Service as an opaque HTTP 500.30 that also takes /health, /diag and the log
            // endpoints offline. Instead let the app boot and report NOT-READY (see /health + /diag/boot),
            // so operators get an actionable signal and can fix RBAC without a black-box crash loop.
            if (environment.IsDevelopment())
                throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

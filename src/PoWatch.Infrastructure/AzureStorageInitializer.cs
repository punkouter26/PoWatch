using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PoWatch.Application.Options;
using PoWatch.Domain.Services;
using PoWatch.Infrastructure.Persistence;

namespace PoWatch.Infrastructure;

/// <summary>
/// One-shot hosted service that ensures all required Azure Storage tables and blob containers
/// exist before the application begins serving requests, and seeds the in-memory slug registry
/// from persisted subjects so that rename collision detection works correctly from the first call.
///
/// Executes when either <see cref="AzureStorageOptions.ConnectionString"/> or
/// <see cref="AzureStorageOptions.ServiceUri"/> is configured; no-ops silently when using in-memory storage.
/// </summary>
public sealed class AzureStorageInitializer(
    AzureStorageClients clients,
    IOptions<AzureStorageOptions> options,
    AzureSubjectRepository subjectRepository,
    IHostEnvironment environment,
    StartupReadiness readiness,
    ILogger<AzureStorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsAzureStorageConfigured(options.Value))
        {
            logger.LogDebug("Azure Storage not configured — using in-memory storage, skipping table initialization.");
            readiness.MarkStorageReady("In-memory storage (Azure Storage not configured).");
            return;
        }

        if (options.Value.SkipStorageInit)
        {
            logger.LogWarning("FeatureFlags:SkipStorageInit=true — skipping Azure Storage initialization. App will use in-memory fallbacks.");
            readiness.MarkStorageReady("Storage initialisation skipped (SkipStorageInit=true).");
            return;
        }

        logger.LogInformation("Initializing Azure Storage tables and containers...");

        try
        {
            var tableService = clients.TableService;

            await tableService.GetTableClient(options.Value.ObservationsTable)
                .CreateIfNotExistsAsync(cancellationToken);

            await tableService.GetTableClient(options.Value.SubjectsTable)
                .CreateIfNotExistsAsync(cancellationToken);

            await clients.BlobService
                .GetBlobContainerClient(options.Value.SignificantImagesContainer)
                .CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            // Seed the in-memory slug registry from all persisted subjects so that
            // SubjectIdSlugger.ResolveCanonicalSubjectId can detect collisions after a restart.
            var subjects = await subjectRepository.GetAllAsync(cancellationToken);
            foreach (var subject in subjects)
                SubjectIdSlugger.RegisterSlug(subject.SubjectId, subject.SubjectId);

            logger.LogInformation(
                "Azure Storage initialization complete. TablesCreated=[{Observations},{Subjects}] SlugsSeed={SlugCount}",
                options.Value.ObservationsTable,
                options.Value.SubjectsTable,
                subjects.Count);

            readiness.MarkStorageReady($"Azure Storage reachable; {subjects.Count} subject slug(s) seeded.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "Azure Storage initialization failed. Storage is configured, so there is NO in-memory fallback — " +
                "reads/writes will fail until the dependency recovers. " +
                "Verify Azurite/Docker is running, or that the Managed Identity holds Storage Table/Blob Data " +
                "Contributor, or set FeatureFlags:SkipStorageInit=true to intentionally skip. " +
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

    private static bool IsAzureStorageConfigured(AzureStorageOptions options) =>
        !string.IsNullOrWhiteSpace(options.ConnectionString)
        || !string.IsNullOrWhiteSpace(options.ServiceUri);
}

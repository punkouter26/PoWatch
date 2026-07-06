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
    ILogger<AzureStorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsAzureStorageConfigured(options.Value))
        {
            logger.LogDebug("Azure Storage not configured — using in-memory storage, skipping table initialization.");
            return;
        }

        if (options.Value.SkipStorageInit)
        {
            logger.LogWarning("FeatureFlags:SkipStorageInit=true — skipping Azure Storage initialization. App will use in-memory fallbacks.");
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
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "Azure Storage initialization failed. Storage is configured, so there is NO in-memory fallback — " +
                "the app would otherwise report a healthy start and then 500 on every read/write. Failing fast. " +
                "Verify Azurite/Docker is running, or set FeatureFlags:SkipStorageInit=true to intentionally skip. " +
                "ServiceUri={ServiceUri} ErrorType={ErrorType} Detail={Detail}",
                options.Value.ServiceUri,
                ex.GetType().Name,
                ex.Message);

            // Fail fast: when storage IS configured, the DI container bound the Azure repositories (not the
            // in-memory ones). A silent "continue" produced a server that looked healthy but was 500-ing every
            // request. Abort startup so orchestrators restart/alert instead of serving a broken instance.
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool IsAzureStorageConfigured(AzureStorageOptions options) =>
        !string.IsNullOrWhiteSpace(options.ConnectionString)
        || !string.IsNullOrWhiteSpace(options.ServiceUri);
}

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
            logger.LogError(ex,
                "Azure Storage initialization failed — app will continue without persistent storage. " +
                "Verify Azurite/Docker is running or set FeatureFlags:SkipStorageInit=true to suppress this. " +
                "ConnectionString={ConnectionString} ServiceUri={ServiceUri} ErrorType={ErrorType} Detail={Detail}",
                options.Value.ConnectionString?.Length > 20
                    ? options.Value.ConnectionString[..20] + "..."
                    : options.Value.ConnectionString,
                options.Value.ServiceUri,
                ex.GetType().Name,
                ex.Message);

            // Non-fatal: the app can still serve requests using in-memory fallbacks.
            // Storage-backed endpoints will fail individually rather than crashing startup.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool IsAzureStorageConfigured(AzureStorageOptions options) =>
        !string.IsNullOrWhiteSpace(options.ConnectionString)
        || !string.IsNullOrWhiteSpace(options.ServiceUri);
}

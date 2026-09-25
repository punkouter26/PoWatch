using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Infrastructure;
using PoWatch.Infrastructure.Persistence;

namespace PoWatch.Unit;

public sealed class InfrastructureDependencyInjectionTests
{
    private static ServiceProvider Build(AzureStorageOptions options)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<AzureStorageOptions>>(Options.Create(options));
        services.AddPoWatchInfrastructure();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Configured_storage_resolves_the_azure_stores_by_connection_string_or_service_uri()
    {
        foreach (var options in new[]
        {
            new AzureStorageOptions { ConnectionString = "UseDevelopmentStorage=true" },
            new AzureStorageOptions { ServiceUri = "https://powatchsa.table.core.windows.net/" }
        })
        {
            using var provider = Build(options);

            Assert.IsType<AzureSessionRepository>(provider.GetRequiredService<ISessionRepository>());
            Assert.IsType<AzureRollupStore>(provider.GetRequiredService<IRollupStore>());
        }
    }

    [Fact]
    public void Unconfigured_storage_falls_back_to_the_in_memory_stores()
    {
        using var provider = Build(new AzureStorageOptions());

        Assert.IsType<InMemorySessionRepository>(provider.GetRequiredService<ISessionRepository>());
        Assert.IsType<InMemoryRollupStore>(provider.GetRequiredService<IRollupStore>());
    }
}

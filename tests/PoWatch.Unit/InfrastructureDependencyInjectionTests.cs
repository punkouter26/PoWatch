using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PoWatch.Application.Contracts;
using PoWatch.Application.Options;
using PoWatch.Infrastructure;
using PoWatch.Infrastructure.Persistence;

namespace PoWatch.Unit;

public sealed class InfrastructureDependencyInjectionTests
{
    [Fact]
    public void AddPoWatchInfrastructure_UsesAzureRepositories_ForDevelopmentStorage()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<AzureObservationRepository>>(NullLogger<AzureObservationRepository>.Instance);
        services.AddSingleton<ILogger<AzureSubjectRepository>>(NullLogger<AzureSubjectRepository>.Instance);
        services.AddSingleton<ILogger<InMemoryObservationRepository>>(NullLogger<InMemoryObservationRepository>.Instance);
        services.AddSingleton<IOptions<AzureStorageOptions>>(Options.Create(new AzureStorageOptions
        {
            ConnectionString = "UseDevelopmentStorage=true"
        }));

        services.AddPoWatchInfrastructure();

        using var serviceProvider = services.BuildServiceProvider();

        var observationRepository = serviceProvider.GetRequiredService<IObservationRepository>();
        var subjectRepository = serviceProvider.GetRequiredService<ISubjectRepository>();

        Assert.IsType<AzureObservationRepository>(observationRepository);
        Assert.IsType<AzureSubjectRepository>(subjectRepository);
    }

    [Fact]
    public void AddPoWatchInfrastructure_UsesAzureRepositories_ForServiceUriConfiguration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<AzureObservationRepository>>(NullLogger<AzureObservationRepository>.Instance);
        services.AddSingleton<ILogger<AzureSubjectRepository>>(NullLogger<AzureSubjectRepository>.Instance);
        services.AddSingleton<ILogger<InMemoryObservationRepository>>(NullLogger<InMemoryObservationRepository>.Instance);
        services.AddSingleton<IOptions<AzureStorageOptions>>(Options.Create(new AzureStorageOptions
        {
            ServiceUri = "https://powatchsa.table.core.windows.net/"
        }));

        services.AddPoWatchInfrastructure();

        using var serviceProvider = services.BuildServiceProvider();

        var observationRepository = serviceProvider.GetRequiredService<IObservationRepository>();
        var subjectRepository = serviceProvider.GetRequiredService<ISubjectRepository>();

        Assert.IsType<AzureObservationRepository>(observationRepository);
        Assert.IsType<AzureSubjectRepository>(subjectRepository);
    }

    [Fact]
    public void AddPoWatchInfrastructure_UsesInMemoryRepositories_WhenStorageIsNotConfigured()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<InMemoryObservationRepository>>(NullLogger<InMemoryObservationRepository>.Instance);
        services.AddSingleton<IOptions<AzureStorageOptions>>(Options.Create(new AzureStorageOptions()));

        services.AddPoWatchInfrastructure();

        using var serviceProvider = services.BuildServiceProvider();

        var observationRepository = serviceProvider.GetRequiredService<IObservationRepository>();
        var subjectRepository = serviceProvider.GetRequiredService<ISubjectRepository>();

        Assert.IsType<InMemoryObservationRepository>(observationRepository);
        Assert.IsType<InMemorySubjectRepository>(subjectRepository);
    }
}

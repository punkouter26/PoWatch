using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace PoWatch.Api.Observability;

public static class TelemetrySetup
{
    /// <summary>
    /// Serilog two-stage initialisation delegate — reads config from appsettings after host is built.
    /// Writes structured logs to Console, a rolling File (logs/powatch-.log), and App Insights when
    /// the ApplicationInsights:ConnectionString setting is present.
    /// </summary>
    public static void ConfigureSerilog(
        HostBuilderContext ctx,
        IServiceProvider services,
        LoggerConfiguration cfg)
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .Enrich.WithProperty("Application", "PoWatch")
           .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
           .WriteTo.Console(
               outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{UserId}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
           .WriteTo.File(
               path: "logs/powatch-.log",
               rollingInterval: RollingInterval.Day,
               retainedFileCountLimit: 30,
               outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] [{UserId}] [{SessionId}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        // AppInsights telemetry handled by AddAzureMonitorTraceExporter() in OTel pipeline.
    }

    /// <summary>
    /// Registers OpenTelemetry tracing with ASP.NET Core instrumentation, a console exporter,
    /// and an Azure Monitor (App Insights) exporter when the connection string is configured.
    /// </summary>
    public static IServiceCollection AddPoWatchTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        var aiConnectionString = configuration["ApplicationInsights:ConnectionString"];

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("PoWatch"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddSource("PoWatch.*")
                    .AddConsoleExporter();

                if (!string.IsNullOrWhiteSpace(aiConnectionString))
                {
                    tracing.AddAzureMonitorTraceExporter(o => o.ConnectionString = aiConnectionString);
                }
            });

        return services;
    }
}

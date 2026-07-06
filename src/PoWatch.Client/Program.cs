using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Options;
using PoWatch.Client;
using PoWatch.Client.Services;
using Radzen;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
// One stable session id per app load, threaded onto every BFF call by CorrelationHandler so the
// server can correlate an entire monitoring session instead of seeing N unrelated requests.
var sessionId = Guid.NewGuid().ToString("N");
builder.Services.AddScoped(sp => new HttpClient(new CorrelationHandler(sessionId) { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = new Uri(string.IsNullOrWhiteSpace(apiBaseUrl)
        ? builder.HostEnvironment.BaseAddress
        : apiBaseUrl)
});
builder.Services.Configure<ClientFeatureFlagsOptions>(builder.Configuration.GetSection("FeatureFlags"));
builder.Services.AddScoped<PoWatchApiClient>();
builder.Services.AddRadzenComponents();

// BFF auth: server cookie holds the session; client derives state from /auth/me (rule 4).
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<BffAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<BffAuthenticationStateProvider>());

// Inference service: mock for dev/testing, WebGPU for production
var flags = builder.Configuration.GetSection("FeatureFlags").Get<ClientFeatureFlagsOptions>() ?? new ClientFeatureFlagsOptions();
if (flags.UseMockAi)
    builder.Services.AddScoped<IInferenceService, MockInferenceService>();
else
    builder.Services.AddScoped<IInferenceService, WebGpuInferenceService>();

// Singleton monitor state shared between NavMenu and ObserverHub
builder.Services.AddSingleton<MonitorStateService>();

await builder.Build().RunAsync();

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
builder.Services.AddScoped(sp => new HttpClient
{
	BaseAddress = new Uri(string.IsNullOrWhiteSpace(apiBaseUrl)
		? builder.HostEnvironment.BaseAddress
		: apiBaseUrl)
});
builder.Services.Configure<ClientFeatureFlagsOptions>(builder.Configuration.GetSection("FeatureFlags"));
builder.Services.AddScoped<PoWatchApiClient>();
builder.Services.Configure<ClientFeatureFlagsOptions>(builder.Configuration.GetSection("FeatureFlags"));
builder.Services.AddRadzenComponents();

await builder.Build().RunAsync();

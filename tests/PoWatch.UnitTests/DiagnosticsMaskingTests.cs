using Microsoft.Extensions.Options;
using PoWatch.Application.Options;
using PoWatch.Infrastructure.Runtime;

namespace PoWatch.Tests;

public sealed class DiagnosticsMaskingTests
{
    [Fact]
    public void MaskMiddle_HidesSensitiveMiddleCharacters()
    {
        var masked = MaskingUtility.MaskMiddle("DEV-LOCAL-KEY-12345");

        Assert.Equal("DEV...345", masked);
        Assert.DoesNotContain("LOCAL-KEY", masked, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CaptureSnapshot_UsesMaskedValues_AndDetectsAzuriteStorage()
    {
        var provider = new LocalDiagnosticsProvider(
            Options.Create(new AzureStorageOptions
            {
                ConnectionString = "UseDevelopmentStorage=true"
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalDiagnosticsProvider>.Instance);

        var snapshot = provider.CaptureSnapshot();

        Assert.Equal("Azurite-OK", snapshot.StorageConnectionStatus);
        Assert.Contains("...", snapshot.MaskedEndpoint);
        Assert.Contains("...", snapshot.MaskedApiKey);
        Assert.DoesNotContain("UseDevelopmentStorage", snapshot.MaskedEndpoint, StringComparison.OrdinalIgnoreCase);
    }
}

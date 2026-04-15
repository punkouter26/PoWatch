using Microsoft.Extensions.Logging;
using PoWatch.Application.Contracts;
using PoWatch.Domain.Models;

namespace PoWatch.Application.Services;

public sealed class DiagnosticsService(
    IDiagnosticsProvider diagnosticsProvider,
    ILogger<DiagnosticsService> logger)
{
    public DiagnosticsSnapshot GetSnapshot()
    {
        logger.LogDebug("Diagnostics snapshot requested.");
        return diagnosticsProvider.CaptureSnapshot();
    }
}

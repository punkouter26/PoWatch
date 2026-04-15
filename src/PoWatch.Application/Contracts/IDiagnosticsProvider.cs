using PoWatch.Domain.Models;

namespace PoWatch.Application.Contracts;

public interface IDiagnosticsProvider
{
    DiagnosticsSnapshot CaptureSnapshot();
}

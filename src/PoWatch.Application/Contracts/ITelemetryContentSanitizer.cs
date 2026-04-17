using PoWatch.Shared.Models;

namespace PoWatch.Application.Contracts;

public interface ITelemetryContentSanitizer
{
    bool TrySanitize(IngestObservationRequestDto input, out IngestObservationRequestDto sanitized, out string reason);
}
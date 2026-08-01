namespace PoWatch.Shared.Models;

/// <summary>
/// Shape of the JSON document served by the <c>/health</c> endpoint. Consumed by the Health
/// Blazor page so the operator-facing view and the machine-readable probe can never disagree
/// about what "healthy" means — both read the same registered checks.
/// </summary>
public sealed record HealthReportDto(
    string Status,
    double DurationMs,
    IReadOnlyList<HealthCheckEntryDto> Checks);

/// <summary>One registered health check — a single connection or dependency.</summary>
public sealed record HealthCheckEntryDto(
    string Name,
    string Status,
    string? Description,
    double DurationMs);

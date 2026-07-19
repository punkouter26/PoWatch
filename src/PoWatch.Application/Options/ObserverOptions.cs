using System.ComponentModel.DataAnnotations;

namespace PoWatch.Application.Options;

public sealed class ObserverOptions
{
    [Range(1, 3600)]
    public int PollingIntervalSeconds { get; init; } = 10;
}

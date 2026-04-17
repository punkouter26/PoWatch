using PoWatch.Domain.Models;

namespace PoWatch.Application.Options;

/// <summary>
/// Configures named alert threshold rules loaded from appsettings.json.
/// Each rule fires when its metric count exceeds Threshold within WindowMinutes.
/// </summary>
public sealed class AlertThresholdOptions
{
    /// <summary>Whether the threshold evaluation engine is active.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The set of named rules. Configured via appsettings.json under "AlertThresholds:Rules".
    /// Defaults to two sensible clinical rules when the config section is absent.
    /// </summary>
    public List<AlertThresholdRule> Rules { get; init; } =
    [
        new AlertThresholdRule
        {
            Name = "Frequent Outliers",
            Metric = AlertMetric.Outlier,
            WindowMinutes = 10,
            Threshold = 3,
            Enabled = true,
            Description = "3 or more clinical outliers detected in 10 minutes."
        },
        new AlertThresholdRule
        {
            Name = "High Activity Burst",
            Metric = AlertMetric.Any,
            WindowMinutes = 5,
            Threshold = 10,
            Enabled = true,
            Description = "10 or more observation events in 5 minutes."
        }
    ];
}

using PoWatch.Domain.Models;

namespace PoWatch.Application.Services;

/// <summary>
/// Maps internal domain models to FHIR R4-compatible JSON structures.
/// Uses plain dictionaries serialised with System.Text.Json — no external FHIR SDK needed.
/// The produced objects conform to the FHIR R4 Observation resource schema (https://hl7.org/fhir/R4/observation.html).
/// </summary>
public sealed class FhirMappingService
{
    private const string FhirVersion = "4.0.1";
    private const string FhirObservationUrl = "http://hl7.org/fhir/StructureDefinition/Observation";

    /// <summary>Maps a single <see cref="ObservationEvent"/> to a FHIR R4 Observation resource dictionary.</summary>
    public Dictionary<string, object?> MapToFhirObservation(ObservationEvent observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        return new Dictionary<string, object?>
        {
            ["resourceType"] = "Observation",
            ["id"] = observation.Id.ToString(),
            ["meta"] = new Dictionary<string, object?>
            {
                ["versionId"] = "1",
                ["lastUpdated"] = observation.ObservedAtUtc.ToString("o"),
                ["profile"] = new[] { FhirObservationUrl }
            },
            ["status"] = "final",
            ["category"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["coding"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["system"] = "http://terminology.hl7.org/CodeSystem/observation-category",
                            ["code"] = "activity",
                            ["display"] = "Activity"
                        }
                    }
                }
            },
            ["code"] = new Dictionary<string, object?>
            {
                ["coding"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["system"] = "https://powatch.local/codesystem/activity",
                        ["code"] = observation.Activity.Replace(" ", "-").ToLowerInvariant(),
                        ["display"] = observation.Activity
                    }
                },
                ["text"] = observation.Activity
            },
            ["subject"] = new Dictionary<string, object?>
            {
                ["reference"] = $"Patient/{observation.SubjectId}",
                ["display"] = observation.SubjectDisplayName
            },
            ["effectiveDateTime"] = observation.ObservedAtUtc.ToString("o"),
            ["note"] = string.IsNullOrWhiteSpace(observation.SignificantReason)
                ? null
                : new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["text"] = observation.SignificantReason
                    }
                },
            ["component"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["code"] = new Dictionary<string, object?>
                    {
                        ["coding"] = new[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["system"] = "https://powatch.local/codesystem/flag",
                                ["code"] = "outlier-flag",
                                ["display"] = "Clinical Outlier Flag"
                            }
                        }
                    },
                    ["valueBoolean"] = observation.IsClinicalOutlier
                }
            }
        };
    }

    /// <summary>Wraps a list of observations in a FHIR R4 Bundle resource (type = searchset).</summary>
    public Dictionary<string, object?> MapToFhirBundle(IEnumerable<ObservationEvent> observations, string bundleId)
    {
        var entries = observations.Select(o => new Dictionary<string, object?>
        {
            ["fullUrl"] = $"urn:uuid:{o.Id}",
            ["resource"] = MapToFhirObservation(o),
            ["search"] = new Dictionary<string, object?> { ["mode"] = "match" }
        }).ToList();

        return new Dictionary<string, object?>
        {
            ["resourceType"] = "Bundle",
            ["id"] = bundleId,
            ["meta"] = new Dictionary<string, object?>
            {
                ["lastUpdated"] = DateTimeOffset.UtcNow.ToString("o")
            },
            ["type"] = "searchset",
            ["total"] = entries.Count,
            ["link"] = Array.Empty<object>(),
            ["entry"] = entries
        };
    }
}

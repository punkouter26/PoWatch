using System.Text.Json.Serialization;
using PoWatch.Domain.Models;

namespace PoWatch.Application.Services;

// ---------------------------------------------------------------------------
// Typed FHIR R4 record hierarchy — compile-time key safety, no anonymous dicts.
// Conforms to https://hl7.org/fhir/R4/observation.html
// ---------------------------------------------------------------------------

public sealed record FhirMeta(
    [property: JsonPropertyName("versionId")] string VersionId,
    [property: JsonPropertyName("lastUpdated")] string LastUpdated,
    [property: JsonPropertyName("profile")] string[] Profile);

public sealed record FhirCoding(
    [property: JsonPropertyName("system")] string System,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("display")] string Display);

public sealed record FhirCodeableConcept(
    [property: JsonPropertyName("coding")] FhirCoding[] Coding,
    [property: JsonPropertyName("text")] string? Text = null);

public sealed record FhirReference(
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("display")] string Display);

public sealed record FhirAnnotation(
    [property: JsonPropertyName("text")] string Text);

public sealed record FhirComponent(
    [property: JsonPropertyName("code")] FhirCodeableConcept Code,
    [property: JsonPropertyName("valueBoolean")] bool ValueBoolean);

public sealed record FhirObservation(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("meta")] FhirMeta Meta,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("category")] FhirCodeableConcept[] Category,
    [property: JsonPropertyName("code")] FhirCodeableConcept Code,
    [property: JsonPropertyName("subject")] FhirReference Subject,
    [property: JsonPropertyName("effectiveDateTime")] string EffectiveDateTime,
    [property: JsonPropertyName("note")] FhirAnnotation[]? Note,
    [property: JsonPropertyName("component")] FhirComponent[] Component);

public sealed record FhirBundleEntry(
    [property: JsonPropertyName("fullUrl")] string FullUrl,
    [property: JsonPropertyName("resource")] FhirObservation Resource,
    [property: JsonPropertyName("search")] FhirBundleEntrySearch Search);

public sealed record FhirBundleEntrySearch(
    [property: JsonPropertyName("mode")] string Mode);

public sealed record FhirBundle(
    [property: JsonPropertyName("resourceType")] string ResourceType,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("meta")] FhirMeta Meta,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("link")] object[] Link,
    [property: JsonPropertyName("entry")] FhirBundleEntry[] Entry);

/// <summary>
/// Maps internal domain models to FHIR R4-compatible objects for serialisation.
/// Uses typed records with <see cref="JsonPropertyNameAttribute"/> — no external FHIR SDK needed.
/// </summary>
public sealed class FhirMappingService
{
    private const string FhirObservationUrl = "http://hl7.org/fhir/StructureDefinition/Observation";

    private static readonly FhirCodeableConcept ActivityCategory = new(
        Coding: [new("http://terminology.hl7.org/CodeSystem/observation-category", "activity", "Activity")]);

    private static readonly FhirCoding OutlierFlagCoding =
        new("https://powatch.local/codesystem/flag", "outlier-flag", "Clinical Outlier Flag");

    /// <summary>Maps a single <see cref="ObservationEvent"/> to a FHIR R4 Observation.</summary>
    public FhirObservation MapToFhirObservation(ObservationEvent observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        return new FhirObservation(
            ResourceType: "Observation",
            Id: observation.Id.ToString(),
            Meta: new FhirMeta("1", observation.ObservedAtUtc.ToString("o"), [FhirObservationUrl]),
            Status: "final",
            Category: [ActivityCategory],
            Code: new FhirCodeableConcept(
                Coding: [new("https://powatch.local/codesystem/activity",
                             observation.Activity.Replace(" ", "-").ToLowerInvariant(),
                             observation.Activity)],
                Text: observation.Activity),
            Subject: new FhirReference($"Patient/{observation.SubjectId}", observation.SubjectDisplayName),
            EffectiveDateTime: observation.ObservedAtUtc.ToString("o"),
            Note: string.IsNullOrWhiteSpace(observation.SignificantReason)
                ? null
                : [new FhirAnnotation(observation.SignificantReason)],
            Component: [new FhirComponent(new FhirCodeableConcept([OutlierFlagCoding]), observation.IsClinicalOutlier)]);
    }

    /// <summary>Wraps a list of observations in a FHIR R4 Bundle (type = searchset).</summary>
    public FhirBundle MapToFhirBundle(IEnumerable<ObservationEvent> observations, string bundleId)
    {
        var entries = observations
            .Select(o => new FhirBundleEntry(
                FullUrl: $"urn:uuid:{o.Id}",
                Resource: MapToFhirObservation(o),
                Search: new FhirBundleEntrySearch("match")))
            .ToArray();

        return new FhirBundle(
            ResourceType: "Bundle",
            Id: bundleId,
            Meta: new FhirMeta("1", DateTimeOffset.UtcNow.ToString("o"), []),
            Type: "searchset",
            Total: entries.Length,
            Link: [],
            Entry: entries);
    }
}

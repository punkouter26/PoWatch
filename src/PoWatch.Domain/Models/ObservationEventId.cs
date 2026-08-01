using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoWatch.Domain.Models;

/// <summary>
/// Strongly-typed identifier for an <see cref="ObservationEvent"/>. Previously a bare
/// <see cref="Guid"/>, which any other Guid in scope could satisfy — including the blob
/// correlation id and the idempotency key that travel alongside it on the ingest path.
/// <para>
/// Conversion in either direction is explicit on purpose. Table Storage RowKeys, DTOs and
/// log properties stay <see cref="string"/>/<see cref="Guid"/>; the cast marks the boundary.
/// </para>
/// </summary>
[JsonConverter(typeof(ObservationEventIdJsonConverter))]
public readonly record struct ObservationEventId(Guid Value)
{
    /// <summary>An unset event id.</summary>
    public static readonly ObservationEventId None = new(Guid.Empty);

    /// <summary>True when the id carries no value.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>Mint a new, unique event id.</summary>
    public static ObservationEventId New() => new(Guid.NewGuid());

    /// <summary>Adopt an existing Guid as an event id (persistence read, DTO mapping edges).</summary>
    public static ObservationEventId From(Guid value) => new(value);

    /// <summary>
    /// Parse a persisted/transported representation. Returns <see cref="None"/> when the input is
    /// absent or malformed, matching how the repository previously fell back on a fresh Guid.
    /// </summary>
    public static ObservationEventId Parse(string? value) =>
        Guid.TryParse(value, out var parsed) ? new(parsed) : None;

    /// <summary>Explicit cast form of <see cref="From(Guid)"/>.</summary>
    public static explicit operator ObservationEventId(Guid value) => From(value);

    /// <summary>Unwrap to the raw Guid at a persistence or transport boundary.</summary>
    public static explicit operator Guid(ObservationEventId id) => id.Value;

    /// <summary>The canonical string form used for RowKeys, logging and DTOs.</summary>
    public override string ToString() => Value.ToString();
}

/// <summary>Serializes <see cref="ObservationEventId"/> as a plain JSON string (not an object).</summary>
public sealed class ObservationEventIdJsonConverter : JsonConverter<ObservationEventId>
{
    public override ObservationEventId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ObservationEventId.Parse(reader.GetString());

    public override void Write(Utf8JsonWriter writer, ObservationEventId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value.ToString());
}

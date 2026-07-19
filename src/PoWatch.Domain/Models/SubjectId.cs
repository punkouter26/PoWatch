using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoWatch.Domain.Models;

/// <summary>
/// Strongly-typed subject identifier (audit #4). Replaces the bare <c>string</c> subject id in the
/// domain models so a subject id can never be silently swapped with an arbitrary string, display name,
/// or other id at a call site. Transport DTOs and Table Storage keys stay <c>string</c>; conversion
/// happens explicitly at those boundaries via <see cref="Value"/> and the implicit string factory.
/// The <see cref="SubjectIdJsonConverter"/> renders it as a plain JSON string, so a domain object that
/// is ever serialized directly keeps the same wire shape as the old string field.
/// </summary>
[JsonConverter(typeof(SubjectIdJsonConverter))]
public readonly record struct SubjectId(string Value)
{
    /// <summary>An unset subject id (empty value).</summary>
    public static readonly SubjectId None = new(string.Empty);

    /// <summary>True when the id carries no value.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>Adopt a raw string as a subject id (ingest, persistence read, DTO mapping edges).</summary>
    public static implicit operator SubjectId(string? value) => new(value ?? string.Empty);

    /// <summary>
    /// Unwrap to the raw string at transport/persistence boundaries. Implicit so the large body of
    /// string-context call sites (Table Storage keys, structured logging, DTO mapping) stay terse.
    /// </summary>
    public static implicit operator string(SubjectId id) => id.Value;

    /// <summary>The underlying string — used for interpolation, logging, and structured properties.</summary>
    public override string ToString() => Value;
}

/// <summary>Serializes <see cref="SubjectId"/> as a plain JSON string (not an object).</summary>
public sealed class SubjectIdJsonConverter : JsonConverter<SubjectId>
{
    public override SubjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, SubjectId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

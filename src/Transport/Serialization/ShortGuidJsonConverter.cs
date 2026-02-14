using System.Text.Json;
using System.Text.Json.Serialization;

using Models;

namespace Transport.Serialization;

/// <summary>
/// Provides JSON serialization support for <see cref="ShortGuid"/>.
/// </summary>
/// <remarks>
/// This converter serializes <see cref="ShortGuid"/> values as JSON strings
/// and deserializes them from either:
/// <list type="bullet">
/// <item>
/// <description>
/// A 22-character URL-safe Base64 representation (short GUID).
/// </description>
/// </item>
/// <item>
/// <description>
/// A standard GUID string format supported by <see cref="Guid.TryParse(string?, out Guid)"/>.
/// </description>
/// </item>
/// </list>
/// </remarks>
public sealed class ShortGuidJsonConverter : JsonConverter<ShortGuid>
{
    /// <summary>
    /// Reads and converts the JSON value to a <see cref="ShortGuid"/> instance.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The parsed <see cref="ShortGuid"/> value.</returns>
    /// <exception cref="JsonException">
    /// Thrown when the JSON token is not a string or when the value cannot be parsed
    /// as either a short or standard GUID.
    /// </exception>
    public override ShortGuid Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("ShortGuid must be represented as a JSON string.");
        }

        var value = reader.GetString();

        if (!ShortGuid.TryParse(value, out var result))
        {
            throw new JsonException($"Invalid ShortGuid value: '{value}'.");
        }

        return result;
    }

    /// <summary>
    /// Writes the <see cref="ShortGuid"/> value as a JSON string.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The <see cref="ShortGuid"/> value to serialize.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(
        Utf8JsonWriter writer,
        ShortGuid value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.ToString());
    }
}

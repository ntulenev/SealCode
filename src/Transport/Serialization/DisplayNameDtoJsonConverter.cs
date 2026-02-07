using System.Text.Json;
using System.Text.Json.Serialization;

using Transport.Models;

namespace Transport.Serialization;

/// <summary>
/// Converts <see cref="DisplayNameDto"/> values to and from JSON string values.
/// </summary>
public sealed class DisplayNameDtoJsonConverter : JsonConverter<DisplayNameDto>
{
    /// <summary>
    /// Reads a <see cref="DisplayNameDto"/> from a JSON string.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">Serialization options to use.</param>
    /// <returns>The parsed <see cref="DisplayNameDto"/>.</returns>
    public override DisplayNameDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("DisplayName must be a string.");
        }

        return new DisplayNameDto(reader.GetString()!);
    }

    /// <summary>
    /// Writes a <see cref="DisplayNameDto"/> as a JSON string.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">Serialization options to use.</param>
    public override void Write(Utf8JsonWriter writer, DisplayNameDto value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

using Transport.Models;

namespace Transport.Serialization;

public sealed class DisplayNameDtoJsonConverter : JsonConverter<DisplayNameDto>
{
    public override DisplayNameDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("DisplayName must be a string.");
        }

        return new DisplayNameDto(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, DisplayNameDto value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}

using System.Text;
using System.Text.Json;

using FluentAssertions;

using Transport.Models;
using Transport.Serialization;

namespace Transport.Tests;

public sealed class DisplayNameDtoJsonConverterTests
{
    [Fact(DisplayName = "ReadShouldThrowWhenTokenIsNotString")]
    [Trait("Category", "Unit")]
    public void ReadShouldThrowWhenTokenIsNotString()
    {
        var converter = new DisplayNameDtoJsonConverter();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("123"));
        reader.Read();

        Exception? exception = null;
        try
        {
            converter.Read(ref reader, typeof(DisplayNameDto), new JsonSerializerOptions());
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        exception.Should().BeOfType<JsonException>();
    }

    [Fact(DisplayName = "ReadShouldReturnDisplayNameDto")]
    [Trait("Category", "Unit")]
    public void ReadShouldReturnDisplayNameDto()
    {
        var converter = new DisplayNameDtoJsonConverter();
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("\"Alice\""));
        reader.Read();

        var result = converter.Read(ref reader, typeof(DisplayNameDto), new JsonSerializerOptions());

        result.Value.Should().Be("Alice");
    }

    [Fact(DisplayName = "WriteShouldWriteStringValue")]
    [Trait("Category", "Unit")]
    public void WriteShouldWriteStringValue()
    {
        var converter = new DisplayNameDtoJsonConverter();
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        converter.Write(writer, new DisplayNameDto("Alice"), new JsonSerializerOptions());
        writer.Flush();

        var json = Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("\"Alice\"");
    }

    [Fact(DisplayName = "WriteShouldThrowWhenWriterIsNull")]
    [Trait("Category", "Unit")]
    public void WriteShouldThrowWhenWriterIsNull()
    {
        var converter = new DisplayNameDtoJsonConverter();

        var action = () => converter.Write(null!, new DisplayNameDto("Alice"), new JsonSerializerOptions());

        action.Should().Throw<ArgumentNullException>();
    }
}

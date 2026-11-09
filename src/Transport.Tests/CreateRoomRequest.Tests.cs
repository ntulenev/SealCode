using System.Text.Json;

using FluentAssertions;

using Transport.Models;

namespace Transport.Tests;

public sealed class CreateRoomRequestTests
{
    [Theory]
    [InlineData(/*lang=json,strict*/ "{\"name\":\"Room\",\"language\":\"sql\"}", "code")]
    [InlineData(/*lang=json,strict*/ "{\"name\":\"Room\",\"kind\":\"whiteboard\"}", "whiteboard")]
    [InlineData(/*lang=json,strict*/ "{\"name\":\"Room\",\"kind\":null}", null)]
    public void JsonCreationPreservesLegacyDefaultsAndSupportsWhiteboards(string json, string? expectedKind)
    {
        var request = JsonSerializer.Deserialize<CreateRoomRequest>(json, JsonSerializerOptions.Web);

        request.Should().NotBeNull();
        request.Name.Should().Be("Room");
        request.Kind.Should().Be(expectedKind);
    }

    [Fact(DisplayName = "CtorShouldSetValues")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetValues()
    {
        var dto = new CreateRoomRequest("Room", "csharp");

        dto.Name.Should().Be("Room");
        dto.Language.Should().Be("csharp");
    }

    [Fact(DisplayName = "CtorShouldAllowNullLanguage")]
    [Trait("Category", "Unit")]
    public void CtorShouldAllowNullLanguage()
    {
        var dto = new CreateRoomRequest("Room", null);

        dto.Language.Should().BeNull();
    }

    [Theory(DisplayName = "CtorShouldThrowWhenNameIsWhiteSpace")]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData(" ")]
    public void CtorShouldThrowWhenNameIsWhiteSpace(string value)
    {
        var action = () => new CreateRoomRequest(value, "csharp");

        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenNameIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenNameIsNull()
    {
        var action = () => new CreateRoomRequest(null!, "csharp");

        action.Should().Throw<ArgumentNullException>();
    }
}

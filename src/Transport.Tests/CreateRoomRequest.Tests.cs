using FluentAssertions;

using Transport.Models;

namespace Transport.Tests;

public sealed class CreateRoomRequestTests
{
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

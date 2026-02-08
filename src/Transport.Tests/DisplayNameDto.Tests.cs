using FluentAssertions;

using Transport.Models;

namespace Transport.Tests;

public sealed class DisplayNameDtoTests
{
    [Fact(DisplayName = "CtorShouldSetValue")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetValue()
    {
        var dto = new DisplayNameDto("Alice");

        dto.Value.Should().Be("Alice");
        dto.ToString().Should().Be("Alice");
    }

    [Theory(DisplayName = "CtorShouldThrowWhenValueIsWhiteSpace")]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData(" ")]
    public void CtorShouldThrowWhenValueIsWhiteSpace(string value)
    {
        var action = () => new DisplayNameDto(value);

        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenValueIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenValueIsNull()
    {
        var action = () => new DisplayNameDto(null!);

        action.Should().Throw<ArgumentNullException>();
    }
}

using FluentAssertions;

namespace Models.Tests;

public sealed class RoomNameTests
{
    [Fact(DisplayName = "CtorShouldTrimValue")]
    [Trait("Category", "Unit")]
    public void CtorShouldTrimValue()
    {
        var name = new RoomName(" Room A ");

        name.Value.Should().Be("Room A");
        name.ToString().Should().Be("Room A");
    }

    [Fact(DisplayName = "CtorShouldTruncateLongValue")]
    [Trait("Category", "Unit")]
    public void CtorShouldTruncateLongValue()
    {
        var name = new RoomName("1234567890123456789012345");

        name.Value.Should().Be("12345678901234567890...");
    }

    [Theory(DisplayName = "CtorShouldThrowWhenValueIsWhiteSpace")]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData(" ")]
    public void CtorShouldThrowWhenValueIsWhiteSpace(string value)
    {
        var action = () => new RoomName(value);

        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenValueIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenValueIsNull()
    {
        var action = () => new RoomName(null!);

        action.Should().Throw<ArgumentNullException>();
    }
}

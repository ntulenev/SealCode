using FluentAssertions;

namespace Models.Tests;

public sealed class RoomIdTests
{
    [Fact(DisplayName = "NewShouldCreateIdentifier")]
    [Trait("Category", "Unit")]
    public void NewShouldCreateIdentifier()
    {
        var roomId = RoomId.New();

        roomId.Value.Should().NotBeNullOrWhiteSpace();
        roomId.Value.Length.Should().Be(32);
        roomId.Value.Should().NotContain("-");
    }

    [Fact(DisplayName = "CtorShouldTrimValue")]
    [Trait("Category", "Unit")]
    public void CtorShouldTrimValue()
    {
        var roomId = new RoomId(" room-1 ");

        roomId.Value.Should().Be("room-1");
        roomId.ToString().Should().Be("room-1");
    }

    [Theory(DisplayName = "CtorShouldThrowWhenValueIsWhiteSpace")]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData(" ")]
    public void CtorShouldThrowWhenValueIsWhiteSpace(string value)
    {
        var action = () => new RoomId(value);

        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenValueIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenValueIsNull()
    {
        var action = () => new RoomId(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Theory(DisplayName = "TryParseShouldReturnFalseWhenValueIsWhiteSpace")]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData(" ")]
    public void TryParseShouldReturnFalseWhenValueIsWhiteSpace(string value)
    {
        var result = RoomId.TryParse(value, out var parsed);

        result.Should().BeFalse();
        parsed.Should().Be(default(RoomId));
    }

    [Fact(DisplayName = "TryParseShouldReturnTrueWhenValueIsValid")]
    [Trait("Category", "Unit")]
    public void TryParseShouldReturnTrueWhenValueIsValid()
    {
        var result = RoomId.TryParse(" room-2 ", out var parsed);

        result.Should().BeTrue();
        parsed.Value.Should().Be("room-2");
    }
}

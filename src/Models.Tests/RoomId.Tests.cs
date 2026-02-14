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
        roomId.Value.Length.Should().Be(22);
        roomId.Value.Should().NotContain("=");
        ShortGuid.TryParse(roomId.Value, out _).Should().BeTrue();
    }

    [Fact(DisplayName = "CtorShouldTrimValue")]
    [Trait("Category", "Unit")]
    public void CtorShouldTrimValue()
    {
        var raw = new ShortGuid(Guid.NewGuid()).ToString();
        var roomId = new RoomId($" {raw} ");

        roomId.Value.Should().Be(raw);
        roomId.ToString().Should().Be(raw);
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
        var raw = new ShortGuid(Guid.NewGuid()).ToString();
        var result = RoomId.TryParse(raw, out var parsed);

        result.Should().BeTrue();
        parsed.Value.Should().Be(raw);
    }
}

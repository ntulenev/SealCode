using FluentAssertions;

namespace Models.Tests;

public sealed class RoomVersionTests
{
    [Fact(DisplayName = "CtorShouldSetValue")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetValue()
    {
        var version = new RoomVersion(2);

        version.Value.Should().Be(2);
    }

    [Theory(DisplayName = "CtorShouldThrowWhenValueIsLessThanOne")]
    [Trait("Category", "Unit")]
    [InlineData(0)]
    [InlineData(-1)]
    public void CtorShouldThrowWhenValueIsLessThanOne(int value)
    {
        var action = () => new RoomVersion(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "NextShouldReturnNextVersion")]
    [Trait("Category", "Unit")]
    public void NextShouldReturnNextVersion()
    {
        var version = new RoomVersion(1);

        var next = version.Next();

        next.Value.Should().Be(2);
    }

    [Fact(DisplayName = "NextShouldRollOverToOneAtIntMaxValue")]
    [Trait("Category", "Unit")]
    public void NextShouldRollOverToOneAtIntMaxValue()
    {
        var version = new RoomVersion(int.MaxValue);

        var next = version.Next();

        next.Value.Should().Be(1);
    }

    [Fact(DisplayName = "ToStringShouldReturnValue")]
    [Trait("Category", "Unit")]
    public void ToStringShouldReturnValue()
    {
        var version = new RoomVersion(12);

        version.ToString().Should().Be("12");
    }
}

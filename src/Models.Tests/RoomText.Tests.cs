using FluentAssertions;

namespace Models.Tests;

public sealed class RoomTextTests
{
    [Fact(DisplayName = "CtorShouldSetValue")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetValue()
    {
        var text = new RoomText("Hello");

        text.Value.Should().Be("Hello");
        text.ToString().Should().Be("Hello");
    }

    [Fact(DisplayName = "CtorShouldThrowWhenValueIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenValueIsNull()
    {
        var action = () => new RoomText(null!);

        action.Should().Throw<ArgumentNullException>();
    }
}

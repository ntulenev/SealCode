using FluentAssertions;

namespace Models.Tests;

public sealed class RoomDeletionReasonTests
{
    [Fact(DisplayName = "CtorShouldTrimValue")]
    [Trait("Category", "Unit")]
    public void CtorShouldTrimValue()
    {
        var reason = new RoomDeletionReason(" closing ");

        reason.Value.Should().Be("closing");
        reason.ToString().Should().Be("closing");
    }

    [Theory(DisplayName = "CtorShouldThrowWhenValueIsWhiteSpace")]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData(" ")]
    public void CtorShouldThrowWhenValueIsWhiteSpace(string value)
    {
        var action = () => new RoomDeletionReason(value);

        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenValueIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenValueIsNull()
    {
        var action = () => new RoomDeletionReason(null!);

        action.Should().Throw<ArgumentNullException>();
    }
}

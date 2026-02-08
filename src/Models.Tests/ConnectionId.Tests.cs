using FluentAssertions;

namespace Models.Tests;

public sealed class ConnectionIdTests
{
    [Fact(DisplayName = "CtorShouldSetValue")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetValue()
    {
        var connectionId = new ConnectionId("conn-1");

        connectionId.Value.Should().Be("conn-1");
        connectionId.ToString().Should().Be("conn-1");
    }

    [Fact(DisplayName = "CtorShouldPreserveWhitespace")]
    [Trait("Category", "Unit")]
    public void CtorShouldPreserveWhitespace()
    {
        var connectionId = new ConnectionId(" conn-1 ");

        connectionId.Value.Should().Be(" conn-1 ");
    }

    [Theory(DisplayName = "CtorShouldThrowWhenValueIsWhiteSpace")]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData(" ")]
    public void CtorShouldThrowWhenValueIsWhiteSpace(string value)
    {
        var action = () => new ConnectionId(value);

        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenValueIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenValueIsNull()
    {
        var action = () => new ConnectionId(null!);

        action.Should().Throw<ArgumentNullException>();
    }
}

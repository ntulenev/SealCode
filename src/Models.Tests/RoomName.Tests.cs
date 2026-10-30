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

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("Собеседование с Иваном Ивановым — Senior .NET Developer — 30.10.2026")]
    [InlineData("Interview with Ada Lovelace — Platform team 🦭")]
    public void CtorShouldPreserveLongValue(string value)
    {
        var name = new RoomName(value);

        name.Value.Should().Be(value);
    }

    [Fact]
    public void CtorShouldAcceptMaximumLengthAfterTrimming()
    {
        var value = new string('я', 160);
        new RoomName($"  {value}  ").Value.Should().Be(value);
    }

    [Theory]
    [InlineData(161)]
    [InlineData(1025)]
    public void CtorShouldRejectOversizedNamesInsteadOfTruncating(int length)
    {
        var action = () => new RoomName(new string('я', length));
        action.Should().Throw<ArgumentException>();
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

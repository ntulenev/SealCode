using FluentAssertions;

namespace Models.Tests;

public sealed class CreatedByTests
{
    [Fact(DisplayName = "CtorShouldTrimValue")]
    [Trait("Category", "Unit")]
    public void CtorShouldTrimValue()
    {
        var createdBy = new CreatedBy(" admin ");

        createdBy.Value.Should().Be("admin");
    }

    [Theory(DisplayName = "CtorShouldThrowWhenValueIsWhiteSpace")]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData(" ")]
    public void CtorShouldThrowWhenValueIsWhiteSpace(string value)
    {
        var action = () => new CreatedBy(value);

        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenValueIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenValueIsNull()
    {
        var action = () => new CreatedBy(null!);

        action.Should().Throw<ArgumentException>();
    }
}

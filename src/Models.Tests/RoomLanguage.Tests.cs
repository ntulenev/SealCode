using FluentAssertions;

namespace Models.Tests;

public sealed class RoomLanguageTests
{
    [Fact(DisplayName = "CtorShouldNormalizeValue")]
    [Trait("Category", "Unit")]
    public void CtorShouldNormalizeValue()
    {
        var language = new RoomLanguage(" CSharp ");

        language.Value.Should().Be("csharp");
        language.ToString().Should().Be("csharp");
    }

    [Fact(DisplayName = "CtorShouldThrowWhenValueIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenValueIsNull()
    {
        var action = () => new RoomLanguage(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenValueIsInvalid")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenValueIsInvalid()
    {
        var action = () => new RoomLanguage("python");

        action.Should().Throw<ArgumentException>();
    }
}

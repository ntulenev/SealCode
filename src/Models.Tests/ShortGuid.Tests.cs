using FluentAssertions;

namespace Models.Tests;

public sealed class ShortGuidTests
{
    [Fact(DisplayName = "Constructor creates short guid with url safe 22 character value")]
    [Trait("Category", "Unit")]
    public void ConstructorCreatesShortGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var shortGuid = new ShortGuid(guid);
        var value = shortGuid.ToString();

        // Assert
        shortGuid.Value.Should().Be(guid);

        value.Should().NotBeNullOrWhiteSpace();
        value.Length.Should().Be(22);
        value.Should().MatchRegex("^[A-Za-z0-9_-]{22}$");
    }

    [Fact(DisplayName = "TryParse succeeds for short guid string")]
    [Trait("Category", "Unit")]
    public void TryParseSucceedsForShortGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var original = new ShortGuid(guid);
        var text = original.ToString();

        // Act
        var result = ShortGuid.TryParse(text, out var parsed);

        // Assert
        result.Should().BeTrue();
        parsed.Value.Should().Be(guid);
        parsed.ToString().Should().Be(text);
    }

    [Fact(DisplayName = "TryParse succeeds for standard guid string")]
    [Trait("Category", "Unit")]
    public void TryParseSucceedsForGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var text = guid.ToString("D");

        // Act
        var result = ShortGuid.TryParse(text, out var parsed);

        // Assert
        result.Should().BeTrue();
        parsed.Value.Should().Be(guid);
        parsed.ToString().Length.Should().Be(22);
    }

    [Theory(DisplayName = "TryParse fails for empty input")]
    [Trait("Category", "Unit")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void TryParseFailsForEmptyInput(string? input)
    {
        // Act
        var result = ShortGuid.TryParse(input, out var parsed);

        // Assert
        result.Should().BeFalse();
        parsed.Should().Be(default(ShortGuid));
    }

    [Theory(DisplayName = "TryParse fails for invalid guid string")]
    [Trait("Category", "Unit")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-00000000000Z")]
    [InlineData("00000000-0000-0000-0000-0000000000000")]
    public void TryParseFailsForInvalidGuid(string input)
    {
        // Act
        var result = ShortGuid.TryParse(input, out var parsed);

        // Assert
        result.Should().BeFalse();
        parsed.Should().Be(default(ShortGuid));
    }

    [Fact(DisplayName = "TryParse fails for invalid short guid format")]
    [Trait("Category", "Unit")]
    public void TryParseFailsForInvalidShortGuid()
    {
        // Arrange
        var invalid = "_____________________*"; // 22 chars, invalid base64 char

        // Act
        var result = ShortGuid.TryParse(invalid, out var parsed);

        // Assert
        result.Should().BeFalse();
        parsed.Should().Be(default(ShortGuid));
    }

    [Fact(DisplayName = "ToGuid returns underlying guid value")]
    [Trait("Category", "Unit")]
    public void ToGuidReturnsGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var shortGuid = new ShortGuid(guid);

        // Act
        var result = shortGuid.ToGuid();

        // Assert
        result.Should().Be(guid);
    }

    [Fact(DisplayName = "FromGuid creates short guid from guid")]
    [Trait("Category", "Unit")]
    public void FromGuidCreatesShortGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        var shortGuid = ShortGuid.FromGuid(guid);

        // Assert
        shortGuid.Value.Should().Be(guid);
        shortGuid.ToString().Length.Should().Be(22);
    }

    [Fact(DisplayName = "Implicit conversion from short guid to guid works")]
    [Trait("Category", "Unit")]
    public void ImplicitConversionToGuidWorks()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var shortGuid = new ShortGuid(guid);

        // Act
        Guid result = shortGuid;

        // Assert
        result.Should().Be(guid);
    }

    [Fact(DisplayName = "Implicit conversion from guid to short guid works")]
    [Trait("Category", "Unit")]
    public void ImplicitConversionFromGuidWorks()
    {
        // Arrange
        var guid = Guid.NewGuid();

        // Act
        ShortGuid shortGuid = guid;

        // Assert
        shortGuid.Value.Should().Be(guid);
    }

    [Fact(DisplayName = "Equality compares underlying guid value")]
    [Trait("Category", "Unit")]
    public void EqualityComparesGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();

        var a = new ShortGuid(guid);
        var b = new ShortGuid(guid);

        // Act & Assert
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact(DisplayName = "Different guid values are not equal")]
    [Trait("Category", "Unit")]
    public void DifferentGuidsAreNotEqual()
    {
        // Arrange
        var a = new ShortGuid(Guid.NewGuid());
        var b = new ShortGuid(Guid.NewGuid());

        // Act & Assert
        a.Equals(b).Should().BeFalse();
    }
}

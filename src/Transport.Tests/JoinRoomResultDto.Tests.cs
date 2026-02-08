using FluentAssertions;

using Transport.Models;

namespace Transport.Tests;

public sealed class JoinRoomResultDtoTests
{
    [Fact(DisplayName = "CtorShouldSetValues")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetValues()
    {
        var users = new[] { new DisplayNameDto("Alice") };

        var dto = new JoinRoomResultDto("Room", "csharp", "text", 1, users, "admin");

        dto.Name.Should().Be("Room");
        dto.Language.Should().Be("csharp");
        dto.Text.Should().Be("text");
        dto.Version.Should().Be(1);
        dto.Users.Should().BeSameAs(users);
        dto.CreatedBy.Should().Be("admin");
    }

    [Fact(DisplayName = "CtorShouldThrowWhenNameIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenNameIsNull()
    {
        var action = () => new JoinRoomResultDto(null!, "csharp", "text", 1, [], "admin");

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenLanguageIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenLanguageIsNull()
    {
        var action = () => new JoinRoomResultDto("Room", null!, "text", 1, [], "admin");

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenTextIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenTextIsNull()
    {
        var action = () => new JoinRoomResultDto("Room", "csharp", null!, 1, [], "admin");

        action.Should().Throw<ArgumentNullException>();
    }

    [Theory(DisplayName = "CtorShouldThrowWhenVersionIsInvalid")]
    [Trait("Category", "Unit")]
    [InlineData(0)]
    [InlineData(-1)]
    public void CtorShouldThrowWhenVersionIsInvalid(int version)
    {
        var action = () => new JoinRoomResultDto("Room", "csharp", "text", version, [], "admin");

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenUsersIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenUsersIsNull()
    {
        var action = () => new JoinRoomResultDto("Room", "csharp", "text", 1, null!, "admin");

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenCreatedByIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenCreatedByIsNull()
    {
        var action = () => new JoinRoomResultDto("Room", "csharp", "text", 1, [], null!);

        action.Should().Throw<ArgumentNullException>();
    }
}

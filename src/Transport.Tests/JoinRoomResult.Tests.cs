using FluentAssertions;

namespace Transport.Tests;

public sealed class JoinRoomResultTests
{
    [Fact(DisplayName = "CtorShouldSetValues")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetValues()
    {
        var users = new[] { "Alice" };

        var result = new JoinRoomResult("Room", "csharp", "text", 2, users, "admin", "state");

        result.Name.Should().Be("Room");
        result.Language.Should().Be("csharp");
        result.Text.Should().Be("text");
        result.Version.Should().Be(2);
        result.Users.Should().BeSameAs(users);
        result.CreatedBy.Should().Be("admin");
        result.YjsState.Should().Be("state");
    }
}

using FluentAssertions;

using Models;

using Transport.Models;

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

    [Fact(DisplayName = "FromShouldMapRoomState")]
    [Trait("Category", "Unit")]
    public void FromShouldMapRoomState()
    {
        var room = new RoomState(
            new RoomId("room-1"),
            new RoomName("Room"),
            new RoomLanguage("csharp"),
            new RoomText("text"),
            new RoomVersion(2),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new AdminUser("admin"),
            [1, 2, 3]);
        room.AddUser(new ConnectionId("conn-2"), new DisplayName("Bob"), 5);
        room.AddUser(new ConnectionId("conn-1"), new DisplayName("Alice"), 5);

        var result = JoinRoomResult.From(room);

        result.Name.Should().Be("Room");
        result.Language.Should().Be("csharp");
        result.Text.Should().Be("text");
        result.Version.Should().Be(2);
        result.Users.Should().Equal(["Alice", "Bob"]);
        result.CreatedBy.Should().Be("admin");
        result.YjsState.Should().Be(Convert.ToBase64String([1, 2, 3]));
    }
}

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

        var updates = new[] { new YjsDocumentUpdate(2, "update") };
        var result = new JoinRoomResult("Room", "csharp", "text", 2, users, "admin", 2, 1, "state", updates);

        result.Name.Should().Be("Room");
        result.Language.Should().Be("csharp");
        result.Text.Should().Be("text");
        result.Version.Should().Be(2);
        result.Users.Should().BeSameAs(users);
        result.CreatedBy.Should().Be("admin");
        result.DocumentRevision.Should().Be(2);
        result.CheckpointRevision.Should().Be(1);
        result.YjsState.Should().Be("state");
        result.YjsUpdates.Should().BeSameAs(updates);
    }

    [Fact(DisplayName = "FromShouldMapRoomState")]
    [Trait("Category", "Unit")]
    public void FromShouldMapRoomState()
    {
        var room = new RoomState(
            RoomId.New(),
            new RoomName("Room"),
            new RoomLanguage("csharp"),
            new RoomText("text"),
            new RoomVersion(2),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new AdminUser("admin"),
            [1, 2, 3]);
        room.AddUser(new ConnectionId("conn-2"), new RoomUser("Bob"), 5);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        room.AppendYjsUpdate([4, 5], DateTimeOffset.UtcNow);

        var result = JoinRoomResult.From(room);

        result.Name.Should().Be("Room");
        result.Language.Should().Be("csharp");
        result.Text.Should().Be("text");
        result.Version.Should().Be(3);
        result.Users.Should().Equal(["Alice", "Bob"]);
        result.CreatedBy.Should().Be("admin");
        result.DocumentRevision.Should().Be(1);
        result.CheckpointRevision.Should().Be(0);
        result.YjsState.Should().Be(Convert.ToBase64String([1, 2, 3]));
        result.YjsUpdates.Should().ContainSingle();
        result.YjsUpdates[0].Revision.Should().Be(1);
        result.YjsUpdates[0].Update.Should().Be(Convert.ToBase64String([4, 5]));
    }
}


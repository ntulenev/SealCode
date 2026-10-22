using Abstractions;

using Logic;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

using Models;

using Moq;

using SealCode.Hub;

namespace SealCode.Tests;

public sealed class RoomHubBroadcastTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("missing")]
    public async Task DisconnectWithoutAnExistingRoomIsHarmless(string? state)
    {
        var room = CreateRoom();
        var manager = Manager(room);
        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        using var hub = CreateHub(room, manager, proxy, new Mock<IGroupManager>());
        if (state is null)
        {
            hub.Context.ClearActiveRoom();
        }
        else
        {
            hub.Context.SetActiveRoom(state == "missing" ? RoomId.New().Value : state, "Alice");
        }
        await hub.OnDisconnectedAsync(null);
        Assert.Equal(1, room.ConnectedUserCount);
        if (state != "invalid")
        {
            Assert.Null(hub.Context.GetActiveRoomId());
        }
        proxy.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DisconnectOfAbsentParticipantClearsContextWithoutBroadcasting()
    {
        var room = CreateRoom();
        room.Leave(new("connection"));
        room.AddUser(new("bob"), new("Bob"), 5);
        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        using var hub = CreateHub(room, Manager(room), proxy, new Mock<IGroupManager>());
        await hub.OnDisconnectedAsync(null);
        Assert.Null(hub.Context.GetActiveRoomId());
        Assert.Equal("Bob", Assert.Single(room.CreateUsersSnapshot()).Value);
        proxy.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("update")]
    [InlineData("language")]
    [InlineData("disconnect")]
    public async Task BroadcastFailureDoesNotUndoCommittedState(string operation)
    {
        var room = CreateRoom();
        var manager = Manager(room);
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("transport failed"));
        using var hub = CreateHub(room, manager, proxy, new Mock<IGroupManager>());
        switch (operation)
        {
            case "update":
                await hub.UpdateYjsAsync(room.RoomId.Value, "AQ==");
                Assert.Equal(1, room.JournalCount);
                break;
            case "language":
                await hub.SetLanguageAsync(room.RoomId.Value, "sql");
                Assert.Equal("sql", room.Language.Value);
                break;
            default:
                await hub.OnDisconnectedAsync(null);
                Assert.Equal(0, room.ConnectedUserCount);
                break;
        }
    }

    [Fact]
    public async Task FailedGroupRemovalNotifiesOnceAndRetainsCleanupContext()
    {
        var room = CreateRoom();
        var manager = Manager(room);
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var groups = new Mock<IGroupManager>();
        groups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("group failed"));
        using var hub = CreateHub(room, manager, proxy, groups);
        await Assert.ThrowsAsync<IOException>(() => hub.LeaveRoomAsync(room.RoomId.Value));
        Assert.Equal(room.RoomId.Value, hub.Context.GetActiveRoomId());
        groups.Reset();
        await hub.OnDisconnectedAsync(null);
        proxy.Verify(p => p.SendCoreAsync("UserLeft", It.IsAny<object?[]>(), CancellationToken.None), Times.Once);
    }

    private static RoomState CreateRoom()
    {
        var room = new RoomState(RoomId.New(), new("Room"), new("csharp"), new(""), new(1), DateTimeOffset.UnixEpoch, new("admin"));
        room.AddUser(new("connection"), new("Alice"), 5);
        return room;
    }

    private static Mock<IRoomManager> Manager(RoomState room)
    {
        var manager = new Mock<IRoomManager>();
        manager.Setup(m => m.TryGetRoom(room.RoomId, out room)).Returns(true);
        return manager;
    }

    private static RoomHub CreateHub(RoomState room, Mock<IRoomManager> manager, Mock<IClientProxy> proxy, Mock<IGroupManager> groups)
    {
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns("connection");
        context.SetupGet(c => c.Items).Returns(new Dictionary<object, object?>());
        context.Object.SetActiveRoom(room.RoomId.Value, "Alice");
        var clients = new Mock<IHubCallerClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
        clients.Setup(c => c.GroupExcept(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>())).Returns(proxy.Object);
        var languages = new Mock<ILanguageValidator>();
        languages.Setup(l => l.IsValid(It.IsAny<RoomLanguage>())).Returns(true);
        return new RoomHub(manager.Object, new RoomSessionService(manager.Object, languages.Object), NullLogger<RoomHub>.Instance)
        {
            Context = context.Object,
            Clients = clients.Object,
            Groups = groups.Object
        };
    }
}

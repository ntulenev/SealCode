using Abstractions;

using FluentAssertions;

using Logic;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

using Models;
using Models.Exceptions;

using Moq;

using SealCode.Hub;

namespace SealCode.Tests;

public sealed class RoomHubTests
{
    [Theory]
    [InlineData("JoinRoom")]
    [InlineData("UpdateYjs")]
    [InlineData("CheckpointYjs")]
    [InlineData("SyncYjs")]
    [InlineData("GetDocumentRevision")]
    [InlineData("GetRoomRevisions")]
    [InlineData("SetLanguage")]
    [InlineData("UpdateCursor")]
    [InlineData("UpdateSelection")]
    [InlineData("UpdateCopy")]
    [InlineData("LeaveRoom")]
    public async Task InvalidRoomIdShouldPreserveExactMessageAcrossMethods(string method)
    {
        using var hub = CreateHub();
        var exception = (await FluentActions.Awaiting(async () =>
        {
            switch (method)
            {
                case "JoinRoom":
                    _ = await hub.JoinRoomAsync("invalid", "Alice");
                    break;
                case "UpdateYjs":
                    await hub.UpdateYjsAsync("invalid", "AQ==");
                    break;
                case "CheckpointYjs":
                    _ = await hub.CheckpointYjsAsync("invalid", "AQ==", "", 0);
                    break;
                case "SyncYjs":
                    _ = hub.SyncYjs("invalid");
                    break;
                case "GetDocumentRevision":
                    _ = hub.GetDocumentRevision("invalid");
                    break;
                case "GetRoomRevisions":
                    _ = hub.GetRoomRevisions("invalid");
                    break;
                case "SetLanguage":
                    await hub.SetLanguageAsync("invalid", "sql");
                    break;
                case "UpdateCursor":
                    await hub.UpdateCursorAsync("invalid", 0);
                    break;
                case "UpdateSelection":
                    await hub.UpdateSelectionAsync("invalid", false);
                    break;
                case "UpdateCopy":
                    await hub.UpdateCopyAsync("invalid");
                    break;
                case "LeaveRoom":
                    await hub.LeaveRoomAsync("invalid");
                    break;
                default:
                    throw new ArgumentException("Unknown test method", nameof(method));
            }
        }).Should().ThrowAsync<HubException>()).Which;
        exception.Message.Should().Be("Invalid room id");
    }

    [Theory]
    [InlineData("UpdateYjs", "!", "Invalid Yjs payload")]
    [InlineData("UpdateYjs", "", "Update payload required")]
    [InlineData("CheckpointYjs", "!", "Invalid Yjs payload")]
    [InlineData("CheckpointYjs", "", "State payload required")]
    public async Task PayloadErrorsShouldPreserveExactMessages(string method, string payload, string expected)
    {
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        SetupTryGetRoom(DefaultRoomId, room, out var manager);
        using var hub = CreateHub(roomManager: manager.Object);

        var exception = (await FluentActions.Awaiting(async () =>
        {
            if (method == "UpdateYjs")
            {
                await hub.UpdateYjsAsync(DefaultRoomId, payload);
            }
            else
            {
                _ = await hub.CheckpointYjsAsync(DefaultRoomId, payload, "", 0);
            }
        }).Should().ThrowAsync<HubException>()).Which;

        exception.Message.Should().Be(expected);
    }

    [Theory]
    [InlineData("Cursor")]
    [InlineData("Selection")]
    [InlineData("Copy")]
    public async Task PresenceFromNonParticipantShouldBeSilent(string operation)
    {
        var room = CreateRoomState(DefaultRoomId);
        SetupTryGetRoom(DefaultRoomId, room, out var manager);
        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        using var hub = CreateHub(roomManager: manager.Object, clients: clients.Object);
        switch (operation)
        {
            case "Cursor":
                await hub.UpdateCursorAsync(DefaultRoomId, 0);
                break;
            case "Selection":
                await hub.UpdateSelectionAsync(DefaultRoomId, false);
                break;
            case "Copy":
                await hub.UpdateCopyAsync(DefaultRoomId);
                break;
            default:
                throw new ArgumentException("Unknown presence operation", nameof(operation));
        }
        clients.VerifyNoOtherCalls();
    }

    private static readonly string DefaultRoomId = RoomId.New().Value;

    [Theory]
    [InlineData("manager")]
    [InlineData("sessions")]
    [InlineData("logger")]
    public void ConstructorRejectsMissingDependencies(string dependency)
    {
        var manager = Mock.Of<IRoomManager>();
        var sessions = Mock.Of<IRoomSessionService>();
        var logger = Mock.Of<ILogger<RoomHub>>();
        var action = () => new RoomHub(dependency == "manager" ? null! : manager,
            dependency == "sessions" ? null! : sessions,
            dependency == "logger" ? null! : logger);
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "JoinRoomAsyncShouldThrowWhenRoomIdIsEmpty")]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldThrowWhenRoomIdIsEmpty()
    {
        using var hub = CreateHub();

        Func<Task> action = () => hub.JoinRoomAsync(" ", "Alice");

        HubErrorAssertions.AssertHubError((await action.Should().ThrowAsync<HubException>()).Which, HubErrorCode.InvalidInput);
    }

    [Fact(DisplayName = "JoinRoomAsyncShouldThrowWhenRoomIsNotFound")]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldThrowWhenRoomIsNotFound()
    {
        var roomManager = new Mock<IRoomManager>(MockBehavior.Strict);
        roomManager.Setup(m => m.RegisterUserInRoom(
                It.IsAny<RoomId>(),
                It.IsAny<ConnectionId>(),
                It.IsAny<RoomUser>()))
            .Throws(new RoomNotFoundException());
        using var hub = CreateHub(roomManager.Object);

        Func<Task> action = () => hub.JoinRoomAsync(DefaultRoomId, "Alice");

        HubErrorAssertions.AssertHubError((await action.Should().ThrowAsync<HubException>()).Which, HubErrorCode.RoomNotFound);
        roomManager.Verify(m => m.RegisterUserInRoom(
                It.IsAny<RoomId>(),
                It.IsAny<ConnectionId>(),
                It.IsAny<RoomUser>()), Times.Once);
    }

    [Theory(DisplayName = "JoinRoomAsyncShouldRejectInvalidRoomUser")]
    [Trait("Category", "Unit")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task JoinRoomAsyncShouldRejectInvalidRoomUser(string? displayName)
    {
        using var hub = CreateHub();

        Func<Task> action = () => hub.JoinRoomAsync(DefaultRoomId, displayName!);

        HubErrorAssertions.AssertHubError((await action.Should().ThrowAsync<HubException>()).Which, HubErrorCode.InvalidInput);
    }

    [Fact(DisplayName = "JoinRoomAsyncShouldAddCallerToGroupAndNotifyOthers")]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldAddCallerToGroupAndNotifyOthers()
    {
        var roomId = DefaultRoomId;
        using var cts = new CancellationTokenSource();
        var room = CreateRoomState(roomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        room.AddUser(new ConnectionId("conn-2"), new RoomUser("Bob"), 5);
        var roomManager = new Mock<IRoomManager>(MockBehavior.Strict);
        roomManager.Setup(m => m.RegisterUserInRoom(
                It.Is<RoomId>(id => id.Value == roomId),
                It.Is<ConnectionId>(id => id.Value == "conn-1"),
                It.Is<RoomUser>(user => user.Value == "Alice")))
            .Returns(room);

        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "UserJoined",
                It.Is<object?[]>(args => MatchUserJoinedArgs(args, "Alice", new[] { "Alice", "Bob" })),
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        clients.Setup(c => c.GroupExcept(
                It.Is<string>(group => group == roomId),
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "conn-1")))
            .Returns(proxy.Object);

        var groups = new Mock<IGroupManager>(MockBehavior.Strict);
        groups.Setup(g => g.AddToGroupAsync(
                It.Is<string>(conn => conn == "conn-1"),
                It.Is<string>(group => group == roomId),
                It.Is<CancellationToken>(token => token == cts.Token)))
            .Returns(Task.CompletedTask);

        var items = new Dictionary<object, object?>();
        using var hub = CreateHub(
            roomManager.Object,
            clients: clients.Object,
            groups: groups.Object,
            items: items,
            cancellationToken: cts.Token);

        var result = await hub.JoinRoomAsync(roomId, " Alice ");

        result.Users.Should().Equal("Alice", "Bob");
        items["roomId"].Should().Be(roomId);
        items["displayName"].Should().Be("Alice");
        roomManager.Verify(m => m.RegisterUserInRoom(
                It.Is<RoomId>(id => id.Value == roomId),
                It.Is<ConnectionId>(id => id.Value == "conn-1"),
                It.Is<RoomUser>(user => user.Value == "Alice")), Times.Once);
        clients.Verify(c => c.GroupExcept(
                It.Is<string>(group => group == roomId),
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "conn-1")), Times.Once);
        proxy.Verify(p => p.SendCoreAsync(
                "UserJoined",
                It.Is<object?[]>(args => MatchUserJoinedArgs(args, "Alice", new[] { "Alice", "Bob" })),
                CancellationToken.None), Times.Once);
        groups.Verify(g => g.AddToGroupAsync(
                It.Is<string>(conn => conn == "conn-1"),
                It.Is<string>(group => group == roomId),
                It.Is<CancellationToken>(token => token == cts.Token)), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldIncludeUpdatesDuringSubscription(bool reconnect)
    {
        using var cts = new CancellationTokenSource();
        var room = CreateRoomState(DefaultRoomId);
        if (reconnect)
        {
            room.AppendAsTestParticipant([1], DateTimeOffset.UnixEpoch);
        }

        var roomManager = new Mock<IRoomManager>(MockBehavior.Strict);
        roomManager.Setup(m => m.RegisterUserInRoom(
                new RoomId(DefaultRoomId), new ConnectionId("conn-1"), new RoomUser("Alice")))
            .Returns(room);

        var subscriptionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriptionCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var groups = new Mock<IGroupManager>(MockBehavior.Strict);
        groups.Setup(g => g.AddToGroupAsync("conn-1", DefaultRoomId, cts.Token))
            .Callback(() => subscriptionStarted.SetResult())
            .Returns(subscriptionCompleted.Task);

        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "UserJoined",
                It.Is<object?[]>(args => MatchUserJoinedArgs(args, "Alice", Array.Empty<string>())),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        clients.Setup(c => c.GroupExcept(DefaultRoomId,
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "conn-1")))
            .Returns(proxy.Object);
        using var hub = CreateHub(roomManager.Object, clients: clients.Object, groups: groups.Object,
            cancellationToken: cts.Token);

        var joinTask = hub.JoinRoomAsync(DefaultRoomId, "Alice");
        await subscriptionStarted.Task;
        var appended = room.AppendAsTestParticipant([2, 3], DateTimeOffset.UnixEpoch);
        subscriptionCompleted.SetResult();
        var result = await joinTask;

        result.DocumentRevision.Should().Be(appended.DocumentRevision.Value);
        result.YjsUpdates.Last().Revision.Should().Be(appended.DocumentRevision.Value);
        result.YjsUpdates.Last().Update.Should().Be(Convert.ToBase64String([2, 3]));
    }

    [Fact(DisplayName = "JoinRoomAsyncShouldThrowWhenAddUserFails")]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldThrowWhenAddUserFails()
    {
        var roomManager = new Mock<IRoomManager>(MockBehavior.Strict);
        roomManager.Setup(m => m.RegisterUserInRoom(
                It.IsAny<RoomId>(),
                It.IsAny<ConnectionId>(),
                It.IsAny<RoomUser>()))
            .Throws(new AddRoomUserException("Display name already in use. Choose another name."));
        using var hub = CreateHub(roomManager.Object);

        Func<Task> action = () => hub.JoinRoomAsync(DefaultRoomId, "Alice");

        HubErrorAssertions.AssertHubError((await action.Should().ThrowAsync<HubException>()).Which, HubErrorCode.InvalidInput);
        roomManager.Verify(m => m.RegisterUserInRoom(
                It.IsAny<RoomId>(),
                It.IsAny<ConnectionId>(),
                It.IsAny<RoomUser>()), Times.Once);
    }

    [Fact(DisplayName = "UpdateYjsAsyncShouldThrowWhenPayloadIsInvalid")]
    [Trait("Category", "Unit")]
    public async Task UpdateYjsAsyncShouldThrowWhenPayloadIsInvalid()
    {
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        SetupTryGetRoom(DefaultRoomId, room, out var roomManager);
        using var hub = CreateHub(roomManager.Object);

        Func<Task> action = () => hub.UpdateYjsAsync(DefaultRoomId, "not-base64");

        HubErrorAssertions.AssertHubError((await action.Should().ThrowAsync<HubException>()).Which, HubErrorCode.InvalidInput);
        roomManager.Verify(m => m.TryGetRoom(It.IsAny<RoomId>(), out room), Times.Exactly(1));
    }

    [Fact(DisplayName = "UpdateYjsAsyncShouldAppendAndBroadcastUpdate")]
    [Trait("Category", "Unit")]
    public async Task UpdateYjsAsyncShouldAppendAndBroadcastUpdate()
    {
        var roomId = DefaultRoomId;
        using var cts = new CancellationTokenSource();
        var updateBase64 = Convert.ToBase64String([1, 2]);
        var room = CreateRoomState(roomId, text: "before");
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        SetupTryGetRoom(roomId, room, out var roomManager);

        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "YjsUpdated",
                It.Is<object?[]>(args => MatchYjsUpdatedArgs(args, updateBase64, 1, 2, "Alice")),
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        clients.Setup(c => c.Group(It.Is<string>(group => group == roomId)))
            .Returns(proxy.Object);

        using var hub = CreateHub(roomManager.Object, clients: clients.Object, cancellationToken: cts.Token);

        await cts.CancelAsync();
        await hub.UpdateYjsAsync(roomId, updateBase64);

        room.Text.Value.Should().Be("before");
        room.Version.Value.Should().Be(2);
        room.DocumentRevision.Value.Should().Be(1);
        room.CreateYjsSyncState().Updates.Should().ContainSingle();
        roomManager.Verify(m => m.TryGetRoom(It.IsAny<RoomId>(), out room), Times.Exactly(1));
        clients.Verify(c => c.Group(It.Is<string>(group => group == roomId)), Times.Once);
        proxy.Verify(p => p.SendCoreAsync(
                "YjsUpdated",
                It.Is<object?[]>(args => MatchYjsUpdatedArgs(args, updateBase64, 1, 2, "Alice")),
                CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Category", "Unit")]
    public async Task CheckpointYjsAsyncShouldAcceptCurrentCheckpoint(bool fullGuid, bool padded)
    {
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        room.AppendAsTestParticipant([1, 2], DateTimeOffset.UnixEpoch);
        SetupTryGetRoom(DefaultRoomId, room, out var roomManager);
        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync("YjsCheckpointed",
            It.Is<object?[]>(args => args.Length == 1 && (long)args[0]! == 1L), CancellationToken.None))
            .Returns(Task.CompletedTask).Verifiable();
        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        clients.Setup(c => c.GroupExcept(DefaultRoomId, It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "conn-1")))
            .Returns(proxy.Object).Verifiable();
        using var hub = CreateHub(roomManager.Object, clients: clients.Object);

        ShortGuid.TryParse(DefaultRoomId, out var guid).Should().BeTrue();
        var inputRoomId = fullGuid ? guid.Value.ToString() : DefaultRoomId;
        if (padded)
        {
            inputRoomId = $"  {inputRoomId}  ";
        }

        var accepted = await hub.CheckpointYjsAsync(
            inputRoomId,
            Convert.ToBase64String([3, 4]),
            "merged",
            1);

        accepted.Should().BeTrue();
        proxy.Verify();
        clients.Verify();
        room.Text.Value.Should().Be("merged");
        room.YjsState.Should().Equal([3, 4]);
        room.YjsCheckpointRevision.Value.Should().Be(1);
        room.CreateYjsSyncState().Updates.Should().BeEmpty();
        roomManager.Verify(m => m.TryGetRoom(It.IsAny<RoomId>(), out room), Times.Exactly(1));
    }

    [Fact]
    public async Task CheckpointBroadcastFailurePreservesAcceptedCheckpoint()
    {
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        room.AppendAsTestParticipant([1, 2], DateTimeOffset.UnixEpoch);
        SetupTryGetRoom(DefaultRoomId, room, out var roomManager);
        var failure = new InvalidOperationException("Broadcast failed");
        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync("YjsCheckpointed",
            It.Is<object?[]>(args => args.Length == 1 && (long)args[0]! == 1L), CancellationToken.None))
            .ThrowsAsync(failure).Verifiable();
        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        clients.Setup(c => c.GroupExcept(DefaultRoomId,
            It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "conn-1")))
            .Returns(proxy.Object).Verifiable();
        var logger = new Mock<ILogger<RoomHub>>();
        using var hub = CreateHub(roomManager.Object, clients: clients.Object, logger: logger.Object);

        var accepted = await hub.CheckpointYjsAsync(DefaultRoomId, "AwQ=", "merged", 1);

        accepted.Should().BeTrue();
        room.Text.Value.Should().Be("merged");
        room.YjsState.Should().Equal([3, 4]);
        room.YjsCheckpointRevision.Value.Should().Be(1);
        room.CreateYjsSyncState().Updates.Should().BeEmpty();
        proxy.Verify();
        clients.Verify();
        logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(DefaultRoomId, StringComparison.Ordinal)),
            failure,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact(DisplayName = "CheckpointYjsAsyncShouldRejectStaleCheckpoint")]
    [Trait("Category", "Unit")]
    public async Task CheckpointYjsAsyncShouldRejectStaleCheckpoint()
    {
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        room.AppendAsTestParticipant([1], DateTimeOffset.UnixEpoch);
        room.AppendAsTestParticipant([2], DateTimeOffset.UnixEpoch);
        SetupTryGetRoom(DefaultRoomId, room, out var roomManager);
        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        using var hub = CreateHub(roomManager.Object, clients: clients.Object);

        var accepted = await hub.CheckpointYjsAsync(
            DefaultRoomId,
            "invalid base64!",
            "stale",
            1);

        accepted.Should().BeFalse();
        clients.VerifyNoOtherCalls();
        room.Text.Value.Should().BeEmpty();
        room.CreateYjsSyncState().Updates.Should().HaveCount(2);
    }

    [Theory(DisplayName = "SetLanguageAsyncShouldThrowWhenLanguageIsInvalid")]
    [Trait("Category", "Unit")]
    [InlineData("javascript")]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SetLanguageAsyncShouldThrowWhenLanguageIsInvalid(string language)
    {
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        SetupTryGetRoom(DefaultRoomId, room, out var roomManager);
        var validator = new Mock<ILanguageValidator>(MockBehavior.Strict);
        validator.Setup(v => v.IsValid(It.IsAny<RoomLanguage>()))
            .Returns(false);
        using var hub = CreateHub(roomManager.Object, validator.Object);

        Func<Task> action = () => hub.SetLanguageAsync(DefaultRoomId, language);

        (await action.Should().ThrowAsync<HubException>()).Which.Message.Should().Be("Invalid language");
        roomManager.Verify(m => m.TryGetRoom(It.IsAny<RoomId>(), out room), Times.Exactly(1));
        validator.Verify(v => v.IsValid(It.IsAny<RoomLanguage>()), Times.Once);
    }

    [Fact(DisplayName = "SetLanguageAsyncShouldBroadcastWhenLanguageIsValid")]
    [Trait("Category", "Unit")]
    public async Task SetLanguageAsyncShouldBroadcastWhenLanguageIsValid()
    {
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new("conn-1"), new("Alice"), 5);
        SetupTryGetRoom(DefaultRoomId, room, out var manager);
        var validator = new Mock<ILanguageValidator>();
        validator.Setup(v => v.IsValid(It.IsAny<RoomLanguage>())).Returns(true);
        using var harness = new HubHarness(manager.Object, validator.Object);
        await harness.Hub.SetLanguageAsync(DefaultRoomId, "JavaScript");
        room.Language.Value.Should().Be("javascript");
        room.Version.Value.Should().Be(2);
        harness.Events.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new SentEvent("LanguageUpdated", ["javascript", 2, 1L]));
        manager.Verify(m => m.TryGetRoom(It.IsAny<RoomId>(), out room), Times.Once);
        validator.Verify(v => v.IsValid(It.IsAny<RoomLanguage>()), Times.Once);
    }

    [Fact(DisplayName = "UpdateCursorAsyncShouldReturnWhenUserNotFound")]
    [Trait("Category", "Unit")]
    public async Task UpdateCursorAsyncShouldReturnWhenUserNotFound()
    {
        var room = CreateRoomState(DefaultRoomId);
        SetupTryGetRoom(DefaultRoomId, room, out var roomManager);
        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        using var hub = CreateHub(roomManager.Object, clients: clients.Object);

        await hub.UpdateCursorAsync(DefaultRoomId, 3);

        roomManager.Verify(m => m.TryGetRoom(It.IsAny<RoomId>(), out room), Times.Exactly(1));
        clients.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "UpdateCursorAsyncShouldBroadcastWhenUserFound")]
    [Trait("Category", "Unit")]
    public async Task UpdateCursorAsyncShouldBroadcastWhenUserFound()
    {
        using var cts = new CancellationTokenSource();
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        SetupTryGetRoom(DefaultRoomId, room, out var roomManager);

        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "CursorUpdated",
                It.Is<object?[]>(args => MatchCursorUpdatedArgs(args, "Alice", 3)),
                It.Is<CancellationToken>(token => token == cts.Token)))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        clients.Setup(c => c.GroupExcept(
                It.Is<string>(group => group == DefaultRoomId),
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "conn-1")))
            .Returns(proxy.Object);

        using var hub = CreateHub(roomManager.Object, clients: clients.Object, cancellationToken: cts.Token);

        await hub.UpdateCursorAsync(DefaultRoomId, 3);

        roomManager.Verify(m => m.TryGetRoom(It.IsAny<RoomId>(), out room), Times.Exactly(1));
        clients.Verify(c => c.GroupExcept(
                It.Is<string>(group => group == DefaultRoomId),
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "conn-1")), Times.Once);
        proxy.Verify(p => p.SendCoreAsync(
                "CursorUpdated",
                It.Is<object?[]>(args => MatchCursorUpdatedArgs(args, "Alice", 3)),
                It.Is<CancellationToken>(token => token == cts.Token)), Times.Once);
    }

    [Fact(DisplayName = "UpdateSelectionAsyncShouldBroadcastWhenUserFound")]
    [Trait("Category", "Unit")]
    public async Task UpdateSelectionAsyncShouldBroadcastWhenUserFound()
    {
        using var cts = new CancellationTokenSource();
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        SetupTryGetRoom(DefaultRoomId, room, out var roomManager);

        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "UserSelection",
                It.Is<object?[]>(args => MatchUserSelectionArgs(args, "Alice", true)),
                It.Is<CancellationToken>(token => token == cts.Token)))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        clients.Setup(c => c.GroupExcept(It.Is<string>(group => group == DefaultRoomId),
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "conn-1")))
            .Returns(proxy.Object);

        using var hub = CreateHub(roomManager.Object, clients: clients.Object, cancellationToken: cts.Token);

        await hub.UpdateSelectionAsync(DefaultRoomId, true);

        roomManager.Verify(m => m.TryGetRoom(It.IsAny<RoomId>(), out room), Times.Exactly(1));
        clients.Verify(c => c.GroupExcept(It.Is<string>(group => group == DefaultRoomId),
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "conn-1")), Times.Once);
        proxy.Verify(p => p.SendCoreAsync(
                "UserSelection",
                It.Is<object?[]>(args => MatchUserSelectionArgs(args, "Alice", true)),
                It.Is<CancellationToken>(token => token == cts.Token)), Times.Once);
    }

    [Fact(DisplayName = "UpdateCopyAsyncShouldBroadcastWhenUserFound")]
    [Trait("Category", "Unit")]
    public async Task UpdateCopyAsyncShouldBroadcastWhenUserFound()
    {
        using var cts = new CancellationTokenSource();
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        SetupTryGetRoom(DefaultRoomId, room, out var roomManager);

        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "UserCopy",
                It.Is<object?[]>(args => MatchUserCopyArgs(args, "Alice")),
                It.Is<CancellationToken>(token => token == cts.Token)))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        clients.Setup(c => c.Group(It.Is<string>(group => group == DefaultRoomId)))
            .Returns(proxy.Object);

        using var hub = CreateHub(roomManager.Object, clients: clients.Object, cancellationToken: cts.Token);

        await hub.UpdateCopyAsync(DefaultRoomId);

        roomManager.Verify(m => m.TryGetRoom(It.IsAny<RoomId>(), out room), Times.Exactly(1));
        clients.Verify(c => c.Group(It.Is<string>(group => group == DefaultRoomId)), Times.Once);
        proxy.Verify(p => p.SendCoreAsync(
                "UserCopy",
                It.Is<object?[]>(args => MatchUserCopyArgs(args, "Alice")),
                It.Is<CancellationToken>(token => token == cts.Token)), Times.Once);
    }

    [Fact(DisplayName = "LeaveRoomAsyncShouldRemoveUserAndNotifyGroup")]
    [Trait("Category", "Unit")]
    public async Task LeaveRoomAsyncShouldRemoveUserAndNotifyGroup()
    {
        using var cts = new CancellationTokenSource();
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        room.AddUser(new ConnectionId("conn-2"), new RoomUser("Bob"), 5);
        SetupTryGetRoom(DefaultRoomId, room, out var roomManager);

        var groups = new Mock<IGroupManager>(MockBehavior.Strict);
        groups.Setup(g => g.RemoveFromGroupAsync(
                It.Is<string>(conn => conn == "conn-1"),
                It.Is<string>(group => group == DefaultRoomId),
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "UserLeft",
                It.Is<object?[]>(args => MatchUserLeftArgs(args, "Alice", new[] { "Bob" })),
                CancellationToken.None))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        clients.Setup(c => c.Group(It.Is<string>(group => group == DefaultRoomId)))
            .Returns(proxy.Object);

        using var hub = CreateHub(
            roomManager.Object,
            clients: clients.Object,
            groups: groups.Object,
            cancellationToken: cts.Token);

        await hub.LeaveRoomAsync(DefaultRoomId);

        room.ConnectedUserCount.Should().Be(1);
        roomManager.Verify(m => m.TryGetRoom(It.IsAny<RoomId>(), out room), Times.Exactly(1));
        clients.Verify(c => c.Group(It.Is<string>(group => group == DefaultRoomId)), Times.Once);
        groups.Verify(g => g.RemoveFromGroupAsync(
                It.Is<string>(conn => conn == "conn-1"),
                It.Is<string>(group => group == DefaultRoomId),
                CancellationToken.None), Times.Once);
        proxy.Verify(p => p.SendCoreAsync(
                "UserLeft",
                It.Is<object?[]>(args => MatchUserLeftArgs(args, "Alice", new[] { "Bob" })),
                CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task FailedGroupSubscriptionRollsBackNewMembership()
    {
        var room = CreateRoomState(DefaultRoomId);
        var manager = new Mock<IRoomManager>();
        manager.Setup(x => x.RegisterUserInRoom(It.IsAny<RoomId>(), It.IsAny<ConnectionId>(), It.IsAny<RoomUser>()))
            .Callback<RoomId, ConnectionId, RoomUser>((_, connection, user) => room.AddUser(connection, user, 5)).Returns(room);
        var groups = new Mock<IGroupManager>();
        groups.Setup(x => x.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("subscription failure"));
        using var hub = CreateHub(manager.Object, groups: groups.Object);
        await FluentActions.Awaiting(() => hub.JoinRoomAsync(DefaultRoomId, "Alice")).Should().ThrowAsync<IOException>();
        room.ConnectedUserCount.Should().Be(0);
        hub.Context.Items.Should().NotContainKey("roomId");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public async Task CheckpointYjsAsyncShouldRejectInvalidDocumentRevision(long revision)
    {
        using var hub = CreateHub();

        Func<Task> action = () => hub.CheckpointYjsAsync(DefaultRoomId, "AQ==", "", revision);

        HubErrorAssertions.AssertHubError((await action.Should().ThrowAsync<HubException>()).Which, HubErrorCode.InvalidInput);
    }

    private static bool MatchUserJoinedArgs(object?[] args, string user, string[] users)
    {
        if (args.Length != 3)
        {
            return false;
        }

        if (!string.Equals(args[0] as string, user, StringComparison.Ordinal))
        {
            return false;
        }

        var actualUsers = args[1] as string[];
        return actualUsers is not null && actualUsers.SequenceEqual(users);
    }

    private static bool MatchYjsUpdatedArgs(
        object?[] args,
        string updateBase64,
        long documentRevision,
        int roomVersion,
        string author)
    {
        if (args.Length != 4)
        {
            return false;
        }

        if (!string.Equals(args[0] as string, updateBase64, StringComparison.Ordinal))
        {
            return false;
        }

        if (args[1] is not long actualDocumentRevision || actualDocumentRevision != documentRevision)
        {
            return false;
        }

        if (args[2] is not int actualRoomVersion || actualRoomVersion != roomVersion)
        {
            return false;
        }

        return string.Equals(args[3] as string, author, StringComparison.Ordinal);
    }

    private static bool MatchCursorUpdatedArgs(
        object?[] args,
        string author,
        int position)
    {
        if (args.Length != 2)
        {
            return false;
        }

        if (!string.Equals(args[0] as string, author, StringComparison.Ordinal))
        {
            return false;
        }

        return args[1] is int actualPosition && actualPosition == position;
    }

    private static bool MatchUserSelectionArgs(
        object?[] args,
        string author,
        bool isMultiLine)
    {
        if (args.Length != 2)
        {
            return false;
        }

        if (!string.Equals(args[0] as string, author, StringComparison.Ordinal))
        {
            return false;
        }

        return args[1] is bool actualIsMultiLine && actualIsMultiLine == isMultiLine;
    }

    private static bool MatchUserCopyArgs(object?[] args, string author)
    {
        if (args.Length != 1)
        {
            return false;
        }

        return string.Equals(args[0] as string, author, StringComparison.Ordinal);
    }

    private static bool MatchUserLeftArgs(object?[] args, string user, string[] users)
    {
        if (args.Length != 3)
        {
            return false;
        }

        if (!string.Equals(args[0] as string, user, StringComparison.Ordinal))
        {
            return false;
        }

        var actualUsers = args[1] as string[];
        return actualUsers is not null && actualUsers.SequenceEqual(users);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Hub ownership is transferred to the caller.")]
    private static RoomHub CreateHub(
        IRoomManager? roomManager = null,
        ILanguageValidator? languageValidator = null,
        IHubCallerClients? clients = null,
        IGroupManager? groups = null,
        IDictionary<object, object?>? items = null,
        ILogger<RoomHub>? logger = null,
        CancellationToken cancellationToken = default)
        => new HubHarness(roomManager, languageValidator, clients, groups, items, logger, cancellationToken).Hub;

    private sealed record SentEvent(string Name, object?[] Arguments);

    private sealed class HubHarness : IDisposable
    {
        internal HubHarness(IRoomManager? roomManager = null, ILanguageValidator? languageValidator = null,
            IHubCallerClients? clients = null, IGroupManager? groups = null,
            IDictionary<object, object?>? items = null, ILogger<RoomHub>? logger = null,
            CancellationToken cancellationToken = default)
        {
            roomManager ??= Mock.Of<IRoomManager>(MockBehavior.Strict);
            languageValidator ??= Mock.Of<ILanguageValidator>(MockBehavior.Strict);
            var proxy = new Mock<IClientProxy>();
            proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Callback<string, object?[], CancellationToken>((name, arguments, _) => Events.Add(new(name, arguments)))
                .Returns(Task.CompletedTask);
            var defaultClients = new Mock<IHubCallerClients>();
            defaultClients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
            defaultClients.Setup(c => c.GroupExcept(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>())).Returns(proxy.Object);
            var context = new Mock<HubCallerContext>(MockBehavior.Strict);
            context.SetupGet(c => c.ConnectionId).Returns("conn-1");
            context.SetupGet(c => c.ConnectionAborted).Returns(cancellationToken);
            context.SetupGet(c => c.Items).Returns(items ?? new Dictionary<object, object?>());
            Hub = new RoomHub(roomManager, new RoomSessionService(roomManager, languageValidator), logger ?? Mock.Of<ILogger<RoomHub>>())
            {
                Context = context.Object,
                Clients = clients ?? defaultClients.Object,
                Groups = groups ?? Mock.Of<IGroupManager>()
            };
        }

        internal RoomHub Hub { get; }

        internal List<SentEvent> Events { get; } = [];

        public void Dispose() => Hub.Dispose();
    }

    private static void SetupTryGetRoom(
        string roomId,
        RoomState room,
        out Mock<IRoomManager> roomManager)
    {
        roomManager = new Mock<IRoomManager>(MockBehavior.Strict);
        roomManager.Setup(m => m.TryGetRoom(It.Is<RoomId>(id => id.Value == roomId), out room))
            .Returns(true);
    }

    private static RoomState CreateRoomState(string roomId, string text = "")
        => new(
            new RoomId(roomId),
            new RoomName("Room"),
            new RoomLanguage("csharp"),
            new RoomText(text),
            new RoomVersion(1),
            DateTimeOffset.UnixEpoch,
            new AdminUser("admin", true));
}


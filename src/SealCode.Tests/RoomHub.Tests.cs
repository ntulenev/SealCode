using Abstractions;

using FluentAssertions;

using Logic;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

using Models;
using Models.Exceptions;

using Moq;

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
        var exception = await Assert.ThrowsAsync<HubException>(async () =>
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
        });
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
        _ = SetupTryGetRoom(DefaultRoomId, room, out var manager);
        using var hub = CreateHub(roomManager: manager.Object);

        var exception = await Assert.ThrowsAsync<HubException>(async () =>
        {
            if (method == "UpdateYjs")
            { await hub.UpdateYjsAsync(DefaultRoomId, payload); }
            else
            { _ = await hub.CheckpointYjsAsync(DefaultRoomId, payload, "", 0); }
        });

        exception.Message.Should().Be(expected);
    }

    [Theory]
    [InlineData("Cursor")]
    [InlineData("Selection")]
    [InlineData("Copy")]
    public async Task PresenceFromNonParticipantShouldBeSilent(string operation)
    {
        var room = CreateRoomState(DefaultRoomId);
        _ = SetupTryGetRoom(DefaultRoomId, room, out var manager);
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

    [Fact(DisplayName = "CtorShouldThrowWhenRoomManagerIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenRoomManagerIsNull()
    {
        var sessions = new Mock<IRoomSessionService>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger<RoomHub>>(MockBehavior.Strict).Object;

        var action = () => new RoomHub(null!, sessions, logger);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenSessionServiceIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenSessionServiceIsNull()
    {
        var roomManager = new Mock<IRoomManager>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger<RoomHub>>().Object;

        var action = () => new RoomHub(roomManager, null!, logger);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenLoggerIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenLoggerIsNull()
    {
        var roomManager = new Mock<IRoomManager>(MockBehavior.Strict).Object;
        var sessions = new Mock<IRoomSessionService>(MockBehavior.Strict).Object;

        var action = () => new RoomHub(roomManager, sessions, null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "JoinRoomAsyncShouldThrowWhenRoomIdIsEmpty")]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldThrowWhenRoomIdIsEmpty()
    {
        using var hub = CreateHub();

        Func<Task> action = () => hub.JoinRoomAsync(" ", "Alice");

        await action.Should().ThrowAsync<HubException>();
    }

    [Fact(DisplayName = "JoinRoomAsyncShouldThrowWhenRoomIsNotFound")]
    [Trait("Category", "Unit")]
    public async Task JoinRoomAsyncShouldThrowWhenRoomIsNotFound()
    {
        var roomManager = new Mock<IRoomManager>(MockBehavior.Strict);
        var registerUserCalls = 0;
        roomManager.Setup(m => m.RegisterUserInRoom(
                It.IsAny<RoomId>(),
                It.IsAny<ConnectionId>(),
                It.IsAny<RoomUser>()))
            .Callback<RoomId, ConnectionId, RoomUser>((_, _, _) => registerUserCalls++)
            .Throws(new RoomNotFoundException());
        using var hub = CreateHub(roomManager.Object);

        Func<Task> action = () => hub.JoinRoomAsync(DefaultRoomId, "Alice");

        await action.Should().ThrowAsync<HubException>();
        registerUserCalls.Should().Be(1);
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

        await action.Should().ThrowAsync<HubException>();
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
        var registerUserCalls = 0;
        roomManager.Setup(m => m.RegisterUserInRoom(
                It.Is<RoomId>(id => id.Value == roomId),
                It.Is<ConnectionId>(id => id.Value == "conn-1"),
                It.Is<RoomUser>(user => user.Value == "Alice")))
            .Callback<RoomId, ConnectionId, RoomUser>((_, _, _) => registerUserCalls++)
            .Returns(room);

        var userJoinedCalls = 0;
        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "UserJoined",
                It.Is<object?[]>(args => MatchUserJoinedArgs(args, "Alice", new[] { "Alice", "Bob" })),
                CancellationToken.None))
            .Callback(() => userJoinedCalls++)
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        var groupExceptCalls = 0;
        clients.Setup(c => c.GroupExcept(
                It.Is<string>(group => group == roomId),
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "conn-1")))
            .Callback(() => groupExceptCalls++)
            .Returns(proxy.Object);

        var addToGroupCalls = 0;
        var groups = new Mock<IGroupManager>(MockBehavior.Strict);
        groups.Setup(g => g.AddToGroupAsync(
                It.Is<string>(conn => conn == "conn-1"),
                It.Is<string>(group => group == roomId),
                It.Is<CancellationToken>(token => token == cts.Token)))
            .Callback(() => addToGroupCalls++)
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
        registerUserCalls.Should().Be(1);
        groupExceptCalls.Should().Be(1);
        userJoinedCalls.Should().Be(1);
        addToGroupCalls.Should().Be(1);
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
            room.AppendYjsUpdate([1], DateTimeOffset.UtcNow);
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
        var appended = room.AppendYjsUpdate([2, 3], DateTimeOffset.UtcNow);
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
        var registerUserCalls = 0;
        roomManager.Setup(m => m.RegisterUserInRoom(
                It.IsAny<RoomId>(),
                It.IsAny<ConnectionId>(),
                It.IsAny<RoomUser>()))
            .Callback<RoomId, ConnectionId, RoomUser>((_, _, _) => registerUserCalls++)
            .Throws(new AddRoomUserException("Display name already in use. Choose another name."));
        using var hub = CreateHub(roomManager.Object);

        Func<Task> action = () => hub.JoinRoomAsync(DefaultRoomId, "Alice");

        await action.Should().ThrowAsync<HubException>();
        registerUserCalls.Should().Be(1);
    }

    [Fact(DisplayName = "UpdateYjsAsyncShouldThrowWhenPayloadIsInvalid")]
    [Trait("Category", "Unit")]
    public async Task UpdateYjsAsyncShouldThrowWhenPayloadIsInvalid()
    {
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        var tryGetRoomCounter = SetupTryGetRoom(DefaultRoomId, room, out var roomManager);
        using var hub = CreateHub(roomManager.Object);

        Func<Task> action = () => hub.UpdateYjsAsync(DefaultRoomId, "not-base64");

        await action.Should().ThrowAsync<HubException>();
        tryGetRoomCounter.Count.Should().Be(1);
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
        var tryGetRoomCounter = SetupTryGetRoom(roomId, room, out var roomManager);

        var yjsUpdatedCalls = 0;
        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "YjsUpdated",
                It.Is<object?[]>(args => MatchYjsUpdatedArgs(args, updateBase64, 1, 2, "Alice")),
                CancellationToken.None))
            .Callback(() => yjsUpdatedCalls++)
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        var groupCalls = 0;
        clients.Setup(c => c.Group(It.Is<string>(group => group == roomId)))
            .Callback(() => groupCalls++)
            .Returns(proxy.Object);

        using var hub = CreateHub(roomManager.Object, clients: clients.Object, cancellationToken: cts.Token);

        await cts.CancelAsync();
        await hub.UpdateYjsAsync(roomId, updateBase64);

        room.Text.Value.Should().Be("before");
        room.Version.Value.Should().Be(2);
        room.DocumentRevision.Value.Should().Be(1);
        room.CreateYjsSyncState().Updates.Should().ContainSingle();
        tryGetRoomCounter.Count.Should().Be(2);
        groupCalls.Should().Be(1);
        yjsUpdatedCalls.Should().Be(1);
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
        room.AppendYjsUpdate([1, 2], DateTimeOffset.UtcNow);
        var tryGetRoomCounter = SetupTryGetRoom(DefaultRoomId, room, out var roomManager);
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
        tryGetRoomCounter.Count.Should().Be(2);
    }

    [Fact]
    public async Task CheckpointBroadcastFailurePreservesAcceptedCheckpoint()
    {
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        room.AppendYjsUpdate([1, 2], DateTimeOffset.UtcNow);
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
        room.AppendYjsUpdate([1], DateTimeOffset.UtcNow);
        room.AppendYjsUpdate([2], DateTimeOffset.UtcNow);
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
        var tryGetRoomCounter = SetupTryGetRoom(DefaultRoomId, room, out var roomManager);
        var validator = new Mock<ILanguageValidator>(MockBehavior.Strict);
        var isValidCalls = 0;
        validator.Setup(v => v.IsValid(It.IsAny<RoomLanguage>()))
            .Callback<RoomLanguage>(_ => isValidCalls++)
            .Returns(false);
        using var hub = CreateHub(roomManager.Object, validator.Object);

        Func<Task> action = () => hub.SetLanguageAsync(DefaultRoomId, language);

        await action.Should().ThrowAsync<HubException>();
        tryGetRoomCounter.Count.Should().Be(1);
        isValidCalls.Should().Be(1);
    }

    [Fact(DisplayName = "SetLanguageAsyncShouldBroadcastWhenLanguageIsValid")]
    [Trait("Category", "Unit")]
    public async Task SetLanguageAsyncShouldBroadcastWhenLanguageIsValid()
    {
        using var cts = new CancellationTokenSource();
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        var tryGetRoomCounter = SetupTryGetRoom(DefaultRoomId, room, out var roomManager);

        var validator = new Mock<ILanguageValidator>(MockBehavior.Strict);
        var isValidCalls = 0;
        validator.Setup(v => v.IsValid(It.Is<RoomLanguage>(language => language.Value == "javascript")))
            .Callback<RoomLanguage>(_ => isValidCalls++)
            .Returns(true);

        var languageUpdatedCalls = 0;
        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "LanguageUpdated",
                It.Is<object?[]>(args => MatchLanguageUpdatedArgs(args, "javascript", 2)),
                CancellationToken.None))
            .Callback(() => languageUpdatedCalls++)
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        var groupCalls = 0;
        clients.Setup(c => c.Group(It.Is<string>(group => group == DefaultRoomId)))
            .Callback(() => groupCalls++)
            .Returns(proxy.Object);

        using var hub = CreateHub(roomManager.Object, validator.Object, clients.Object, cancellationToken: cts.Token);

        await hub.SetLanguageAsync(DefaultRoomId, "JavaScript");

        room.Language.Value.Should().Be("javascript");
        room.Version.Value.Should().Be(2);
        tryGetRoomCounter.Count.Should().Be(1);
        isValidCalls.Should().Be(1);
        groupCalls.Should().Be(1);
        languageUpdatedCalls.Should().Be(1);
    }

    [Fact(DisplayName = "UpdateCursorAsyncShouldReturnWhenUserNotFound")]
    [Trait("Category", "Unit")]
    public async Task UpdateCursorAsyncShouldReturnWhenUserNotFound()
    {
        var room = CreateRoomState(DefaultRoomId);
        var tryGetRoomCounter = SetupTryGetRoom(DefaultRoomId, room, out var roomManager);
        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        using var hub = CreateHub(roomManager.Object, clients: clients.Object);

        await hub.UpdateCursorAsync(DefaultRoomId, 3);

        tryGetRoomCounter.Count.Should().Be(1);
        clients.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "UpdateCursorAsyncShouldBroadcastWhenUserFound")]
    [Trait("Category", "Unit")]
    public async Task UpdateCursorAsyncShouldBroadcastWhenUserFound()
    {
        using var cts = new CancellationTokenSource();
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        var tryGetRoomCounter = SetupTryGetRoom(DefaultRoomId, room, out var roomManager);

        var cursorUpdatedCalls = 0;
        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "CursorUpdated",
                It.Is<object?[]>(args => MatchCursorUpdatedArgs(args, "Alice", 3)),
                It.Is<CancellationToken>(token => token == cts.Token)))
            .Callback(() => cursorUpdatedCalls++)
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        var groupExceptCalls = 0;
        clients.Setup(c => c.GroupExcept(
                It.Is<string>(group => group == DefaultRoomId),
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "conn-1")))
            .Callback(() => groupExceptCalls++)
            .Returns(proxy.Object);

        using var hub = CreateHub(roomManager.Object, clients: clients.Object, cancellationToken: cts.Token);

        await hub.UpdateCursorAsync(DefaultRoomId, 3);

        tryGetRoomCounter.Count.Should().Be(1);
        groupExceptCalls.Should().Be(1);
        cursorUpdatedCalls.Should().Be(1);
    }

    [Fact(DisplayName = "UpdateSelectionAsyncShouldBroadcastWhenUserFound")]
    [Trait("Category", "Unit")]
    public async Task UpdateSelectionAsyncShouldBroadcastWhenUserFound()
    {
        using var cts = new CancellationTokenSource();
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        var tryGetRoomCounter = SetupTryGetRoom(DefaultRoomId, room, out var roomManager);

        var userSelectionCalls = 0;
        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "UserSelection",
                It.Is<object?[]>(args => MatchUserSelectionArgs(args, "Alice", true)),
                It.Is<CancellationToken>(token => token == cts.Token)))
            .Callback(() => userSelectionCalls++)
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        var groupCalls = 0;
        clients.Setup(c => c.GroupExcept(It.Is<string>(group => group == DefaultRoomId),
                It.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "conn-1")))
            .Callback(() => groupCalls++)
            .Returns(proxy.Object);

        using var hub = CreateHub(roomManager.Object, clients: clients.Object, cancellationToken: cts.Token);

        await hub.UpdateSelectionAsync(DefaultRoomId, true);

        tryGetRoomCounter.Count.Should().Be(1);
        groupCalls.Should().Be(1);
        userSelectionCalls.Should().Be(1);
    }

    [Fact(DisplayName = "UpdateCopyAsyncShouldBroadcastWhenUserFound")]
    [Trait("Category", "Unit")]
    public async Task UpdateCopyAsyncShouldBroadcastWhenUserFound()
    {
        using var cts = new CancellationTokenSource();
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        var tryGetRoomCounter = SetupTryGetRoom(DefaultRoomId, room, out var roomManager);

        var userCopyCalls = 0;
        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "UserCopy",
                It.Is<object?[]>(args => MatchUserCopyArgs(args, "Alice")),
                It.Is<CancellationToken>(token => token == cts.Token)))
            .Callback(() => userCopyCalls++)
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        var groupCalls = 0;
        clients.Setup(c => c.Group(It.Is<string>(group => group == DefaultRoomId)))
            .Callback(() => groupCalls++)
            .Returns(proxy.Object);

        using var hub = CreateHub(roomManager.Object, clients: clients.Object, cancellationToken: cts.Token);

        await hub.UpdateCopyAsync(DefaultRoomId);

        tryGetRoomCounter.Count.Should().Be(1);
        groupCalls.Should().Be(1);
        userCopyCalls.Should().Be(1);
    }

    [Fact(DisplayName = "LeaveRoomAsyncShouldRemoveUserAndNotifyGroup")]
    [Trait("Category", "Unit")]
    public async Task LeaveRoomAsyncShouldRemoveUserAndNotifyGroup()
    {
        using var cts = new CancellationTokenSource();
        var room = CreateRoomState(DefaultRoomId);
        room.AddUser(new ConnectionId("conn-1"), new RoomUser("Alice"), 5);
        room.AddUser(new ConnectionId("conn-2"), new RoomUser("Bob"), 5);
        var tryGetRoomCounter = SetupTryGetRoom(DefaultRoomId, room, out var roomManager);

        var removeFromGroupCalls = 0;
        var groups = new Mock<IGroupManager>(MockBehavior.Strict);
        groups.Setup(g => g.RemoveFromGroupAsync(
                It.Is<string>(conn => conn == "conn-1"),
                It.Is<string>(group => group == DefaultRoomId),
                CancellationToken.None))
            .Callback(() => removeFromGroupCalls++)
            .Returns(Task.CompletedTask);

        var userLeftCalls = 0;
        var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
        proxy.Setup(p => p.SendCoreAsync(
                "UserLeft",
                It.Is<object?[]>(args => MatchUserLeftArgs(args, "Alice", new[] { "Bob" })),
                CancellationToken.None))
            .Callback(() => userLeftCalls++)
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubCallerClients>(MockBehavior.Strict);
        var groupCalls = 0;
        clients.Setup(c => c.Group(It.Is<string>(group => group == DefaultRoomId)))
            .Callback(() => groupCalls++)
            .Returns(proxy.Object);

        using var hub = CreateHub(
            roomManager.Object,
            clients: clients.Object,
            groups: groups.Object,
            cancellationToken: cts.Token);

        await hub.LeaveRoomAsync(DefaultRoomId);

        room.ConnectedUserCount.Should().Be(1);
        tryGetRoomCounter.Count.Should().Be(1);
        groupCalls.Should().Be(1);
        removeFromGroupCalls.Should().Be(1);
        userLeftCalls.Should().Be(1);
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
        await Assert.ThrowsAsync<IOException>(() => hub.JoinRoomAsync(DefaultRoomId, "Alice"));
        Assert.Equal(0, room.ConnectedUserCount);
        Assert.False(hub.Context.Items.ContainsKey("roomId"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public async Task CheckpointYjsAsyncShouldRejectInvalidDocumentRevision(long revision)
    {
        using var hub = CreateHub();

        Func<Task> action = () => hub.CheckpointYjsAsync(DefaultRoomId, "AQ==", "", revision);

        await action.Should().ThrowAsync<HubException>();
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

    private static bool MatchLanguageUpdatedArgs(
        object?[] args,
        string language,
        int version)
    {
        if (args.Length != 3)
        {
            return false;
        }

        if (!string.Equals(args[0] as string, language, StringComparison.Ordinal))
        {
            return false;
        }

        return args[1] is int actualVersion && actualVersion == version;
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

    private static RoomHub CreateHub(
        IRoomManager? roomManager = null,
        ILanguageValidator? languageValidator = null,
        IHubCallerClients? clients = null,
        IGroupManager? groups = null,
        IDictionary<object, object?>? items = null,
        ILogger<RoomHub>? logger = null,
        CancellationToken cancellationToken = default)
    {
        roomManager ??= new Mock<IRoomManager>(MockBehavior.Strict).Object;
        languageValidator ??= new Mock<ILanguageValidator>(MockBehavior.Strict).Object;
        clients ??= new Mock<IHubCallerClients>(MockBehavior.Strict).Object;
        groups ??= new Mock<IGroupManager>(MockBehavior.Strict).Object;
        items ??= new Dictionary<object, object?>();
        logger ??= new Mock<ILogger<RoomHub>>().Object;
        var context = new Mock<HubCallerContext>(MockBehavior.Strict);
        context.SetupGet(c => c.ConnectionId).Returns("conn-1");
        context.SetupGet(c => c.ConnectionAborted).Returns(cancellationToken);
        context.SetupGet(c => c.Items).Returns(items);

        var hub = new RoomHub(roomManager, new RoomSessionService(roomManager, languageValidator), logger)
        {
            Context = context.Object,
            Clients = clients,
            Groups = groups
        };

        return hub;
    }

    private static CallCounter SetupTryGetRoom(
        string roomId,
        RoomState room,
        out Mock<IRoomManager> roomManager)
    {
        var counter = new CallCounter();
        roomManager = new Mock<IRoomManager>(MockBehavior.Strict);
        roomManager.Setup(m => m.TryGetRoom(It.Is<RoomId>(id => id.Value == roomId), out room))
            .Callback(() => counter.Count++)
            .Returns(true);
        return counter;
    }

    private sealed class CallCounter
    {
        public int Count { get; set; }
    }

    private static RoomState CreateRoomState(string roomId, string text = "")
        => new(
            new RoomId(roomId),
            new RoomName("Room"),
            new RoomLanguage("csharp"),
            new RoomText(text),
            new RoomVersion(1),
            DateTimeOffset.UtcNow,
            new AdminUser("admin", true));
}


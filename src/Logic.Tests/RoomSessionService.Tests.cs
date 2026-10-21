using Abstractions;

using Models;
using Models.Configuration;
using Models.Exceptions;

using Moq;

namespace Logic.Tests;

public sealed class RoomSessionServiceTests
{
    private readonly ConnectionId _connection = new("connection");

    private (RoomState Room, RoomSessionService Service) Create()
    {
        var room = new RoomState(RoomId.New(), new("Room"), new("csharp"), new(""), new(1), DateTimeOffset.UnixEpoch,
            new("admin"), limits: new RoomLimits { MaxEncodedUpdateBytes = 4, MaxEncodedCheckpointBytes = 4, MaxSnapshotCharacters = 10 });
        room.AddUser(_connection, new("Alice"), 5);
        var manager = new Mock<IRoomManager>(MockBehavior.Strict);
        manager.Setup(value => value.TryGetRoom(room.RoomId, out room)).Returns(true);
        return (room, new RoomSessionService(manager.Object, Mock.Of<ILanguageValidator>()));
    }

    [Fact]
    public void EncodedLengthIsCheckedBeforeDecoding()
    {
        var (room, service) = Create();
        Assert.Throws<RoomLimitException>(() => service.Append(room.RoomId, _connection, "!!!!!"));
        Assert.Throws<RoomLimitException>(() => service.Checkpoint(room.RoomId, _connection, "!!!!!", "", 0));
        Assert.Equal(0, room.DocumentRevision.Value);
    }

    [Fact]
    public void InvalidBase64IsAnArgumentError()
    {
        var (room, service) = Create();
        Assert.Equal("Invalid Yjs payload", Assert.Throws<ArgumentException>(() => service.Append(room.RoomId, _connection, "!")).Message);
        Assert.Equal("Invalid Yjs payload", Assert.Throws<ArgumentException>(() => service.Checkpoint(room.RoomId, _connection, "!", "", 0)).Message);
    }

    [Fact]
    public void StaleCheckpointIsRejectedWithoutDecoding()
    {
        var (room, service) = Create();
        _ = service.Append(room.RoomId, _connection, "AQ==");
        Assert.Equal(CheckpointOutcome.Stale, service.Checkpoint(room.RoomId, _connection, "!", "", 0));
        Assert.Equal(1, room.JournalCount);
    }

    [Fact]
    public void AcceptedUpdatesAndCheckpointsPreserveDocumentState()
    {
        var (room, service) = Create();
        var append = service.Append(room.RoomId, _connection, "AQ==");
        Assert.Equal(1, append.DocumentRevision.Value);
        Assert.Equal(CheckpointOutcome.Accepted, service.Checkpoint(room.RoomId, _connection, "Ag==", "text", 1));
        Assert.Equal("text", room.Text.Value);
        Assert.Equal(new byte[] { 2 }, room.YjsState);
        Assert.Equal(0, room.JournalCount);
    }

    [Fact]
    public void TryGetParticipantDistinguishesNonMemberFromMissingRoom()
    {
        var (room, service) = Create();
        Assert.False(service.TryGetParticipant(room.RoomId, new("outsider"), out var found, out _));
        Assert.Same(room, found);
        Assert.True(service.TryGetParticipant(room.RoomId, _connection, out _, out var user));
        Assert.Equal("Alice", user.Value);
        var manager = new Mock<IRoomManager>();
        var missing = new RoomSessionService(manager.Object, Mock.Of<ILanguageValidator>());
        Assert.Throws<RoomNotFoundException>(() => missing.TryGetParticipant(RoomId.New(), _connection, out _, out _));
    }
}

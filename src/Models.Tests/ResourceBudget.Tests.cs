using Models.Configuration;
using Models.Exceptions;

namespace Models.Tests;

public sealed class ResourceBudgetTests
{
    [Fact]
    public void NewConnectionLimitDefaultsToThirty()
    {
        Assert.Equal(30, new RoomLimits().NewConnectionsPerSourcePerMinute);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    public void NewConnectionLimitRejectsOutOfRangeValues(int value)
    {
        var limits = new RoomLimits { NewConnectionsPerSourcePerMinute = value };
        Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            limits, new(limits), [], validateAllProperties: true));
    }

    private static RoomState Create(RoomLimits limits) => new(RoomId.New(), new("Room"), new("csharp"), new(""), new(1), DateTimeOffset.UnixEpoch, new("admin"), limits: limits);

    [Theory]
    [InlineData(1023, true)]
    [InlineData(1024, true)]
    [InlineData(1025, false)]
    public void JournalBoundaryDoesNotMutateOnRefusal(int bytes, bool accepted)
    {
        var room = Create(new()
        {
            MaxJournalBytes = 1024
        });
        if (accepted)
        {
            room.AppendYjsUpdate(new byte[bytes], DateTimeOffset.UtcNow);
        }
        else
        {
            Assert.Throws<RoomLimitException>(() => room.AppendYjsUpdate(new byte[bytes], DateTimeOffset.UtcNow));
        }

        Assert.Equal(accepted ? 1 : 0, room.DocumentRevision.Value);
        Assert.Equal(accepted ? bytes : 0, room.JournalBytes);
        if (!accepted)
        {
            Assert.Equal(DateTimeOffset.UnixEpoch, room.LastUpdatedUtc);
        }
    }

    [Fact]
    public void ConcurrentAppendsCannotExceedBudgetAndCheckpointReleasesIt()
    {
        var room = Create(new()
        {
            MaxJournalBytes = 1024,
            MaxJournalCount = 4
        });
        Parallel.For(0, 32, _ =>
        {
            try
            {
                room.AppendYjsUpdate(new byte[256], DateTimeOffset.UtcNow);
            }
            catch (RoomLimitException) { }
        });
        Assert.Equal(4, room.DocumentRevision.Value);
        Assert.Equal(1024, room.JournalBytes);
        Assert.True(room.TryCreateYjsCheckpoint(new(4), [1], new(""), DateTimeOffset.UtcNow));
        room.AppendYjsUpdate([2], DateTimeOffset.UtcNow);
        Assert.Equal(5, room.DocumentRevision.Value);
        Assert.Equal(1, room.JournalCount);
    }

    [Fact]
    public void SnapshotIsDetachedAndIncludesIndependentMetadataAndPresenceRevisions()
    {
        var room = Create(new());
        room.AddUser(new("a"), new("Alice"), 5);
        room.AppendYjsUpdate([1, 2], DateTimeOffset.UtcNow);
        var snapshot = room.CreateYjsSyncState();
        room.AddUser(new("b"), new("Bob"), 5);
        room.AppendYjsUpdate([3], DateTimeOffset.UtcNow);
        snapshot.Updates[0].Payload[0] = 99;
        Assert.Equal(1, snapshot.DocumentRevision.Value);
        Assert.Equal(2, snapshot.Version.Value);
        Assert.Equal(1, snapshot.PresenceRevision);
        Assert.Single(snapshot.Users);
        Assert.Equal(1, room.CreateYjsSyncState().Updates[0].Payload[0]);
    }

    [Fact]
    public void RepeatedJoinIsIdempotentAndRenameIsRejected()
    {
        var room = Create(new());
        room.AddUser(new("a"), new("Alice"), 5);
        room.AddUser(new("a"), new("Alice"), 5);
        Assert.Equal(1, room.PresenceRevision);
        Assert.Throws<AddRoomUserException>(() => room.AddUser(new("a"), new("Bob"), 5));
        Assert.Equal("Alice", room.ConnectedUsers[new("a")].Value);
    }

    [Fact]
    public void LargeDocumentFitsDefaultBudgets()
    {
        var room = Create(new());
        room.AppendYjsUpdate(new byte[500100], DateTimeOffset.UtcNow);
        Assert.True(room.TryCreateYjsCheckpoint(new(1), new byte[500100], new(new string('a', 500000)), DateTimeOffset.UtcNow));
    }
}

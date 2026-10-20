using Models.Configuration;
using Models.Exceptions;

namespace Models.Tests;

public sealed class ResourceBudgetTests
{
    private static RoomState Create(RoomLimits limits) => new(RoomId.New(), new("Room"), new("csharp"), new(""), new(1), DateTimeOffset.UnixEpoch, new("admin"), limits: limits);

    [Theory]
    [InlineData(1023, true)]
    [InlineData(1024, true)]
    [InlineData(1025, false)]
    public void JournalBoundaryDoesNotMutateOnRefusal(int bytes, bool accepted)
    {
        var room = Create(new() { MaxJournalBytes = 1024 });
        if (accepted) room.AppendYjsUpdate(new byte[bytes], DateTimeOffset.UtcNow);
        else Assert.Throws<RoomLimitException>(() => room.AppendYjsUpdate(new byte[bytes], DateTimeOffset.UtcNow));
        Assert.Equal(accepted ? 1 : 0, room.DocumentRevision.Value);
        Assert.Equal(accepted ? bytes : 0, room.JournalBytes);
        if (!accepted) Assert.Equal(DateTimeOffset.UnixEpoch, room.LastUpdatedUtc);
    }

    [Fact]
    public void ConcurrentAppendsCannotExceedBudgetAndCheckpointReleasesIt()
    {
        var room = Create(new() { MaxJournalBytes = 1024, MaxJournalCount = 4 });
        Parallel.For(0, 32, _ => {
            try { room.AppendYjsUpdate(new byte[256], DateTimeOffset.UtcNow); }
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
    public void LargeDocumentFitsDefaultBudgets()
    {
        var room = Create(new());
        room.AppendYjsUpdate(new byte[500100], DateTimeOffset.UtcNow);
        Assert.True(room.TryCreateYjsCheckpoint(new(1), new byte[500100], new(new string('a', 500000)), DateTimeOffset.UtcNow));
    }
}

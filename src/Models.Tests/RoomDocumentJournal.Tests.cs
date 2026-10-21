using System.Diagnostics.Metrics;

using Models.Configuration;
using Models.Exceptions;

namespace Models.Tests;

[CollectionDefinition("Room journal metrics", DisableParallelization = true)]
public sealed class RoomJournalMetricsGroup
{
}

[Collection("Room journal metrics")]
public sealed class RoomDocumentJournalTests
{
    [Fact]
    public void JournalLimitsRejectWithoutChangingRevisionsAndCheckpointRestoresCapacity()
    {
        var journal = new RoomDocumentJournal(new RoomLimits
        {
            MaxEncodedUpdateBytes = 4,
            MaxJournalBytes = 3,
            MaxJournalCount = 2,
            MaxEncodedCheckpointBytes = 4,
            MaxSnapshotCharacters = 3
        }, null);
        _ = journal.Append([1]);
        _ = journal.Append([2]);
        Assert.Throws<RoomLimitException>(() => journal.Append([3]));
        Assert.Equal(2, journal.DocumentRevision.Value);
        Assert.Equal(2, journal.Bytes);
        Assert.False(journal.TryCheckpoint(new(1), [9], new("")));
        Assert.Equal(2, journal.Count);
        Assert.Throws<RoomLimitException>(() => journal.TryCheckpoint(new(2), new byte[4], new("")));
        Assert.Throws<RoomLimitException>(() => journal.TryCheckpoint(new(2), [9], new("text")));
        Assert.True(journal.TryCheckpoint(new(2), [9], new("txt")));
        Assert.Equal(0, journal.Bytes);
        Assert.Equal(0, journal.Count);
        _ = journal.Append([3]);
        Assert.Equal(3, journal.DocumentRevision.Value);
        Assert.Equal(2, journal.YjsCheckpointRevision.Value);
        journal.Clear();
    }

    [Fact]
    public void JournalSnapshotsOwnCopiesOfCheckpointAndUpdates()
    {
        var initial = new byte[] { 1 };
        var journal = new RoomDocumentJournal(new RoomLimits(), initial);
        initial[0] = 9;
        var update = new byte[] { 2 };
        _ = journal.Append(update);
        update[0] = 9;
        var snapshot = journal.CreateSnapshot();
        Assert.Equal(new byte[] { 1 }, snapshot.Checkpoint);
        Assert.Equal(new byte[] { 2 }, snapshot.Updates[0].Payload);
        snapshot.Checkpoint[0] = 8;
        Assert.Equal(new byte[] { 1 }, journal.State);
        journal.Clear();
    }

    [Fact]
    public void AppendCheckpointAndCloseReturnMetricBalancesToBaseline()
    {
        long bytes = 0;
        long count = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == "SealCode.Rooms")
            {
                owner.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "room.journal.bytes")
            {
                bytes += value;
            }
            if (instrument.Name == "room.journal.count")
            {
                count += value;
            }
        });
        listener.Start();
        var room = new RoomState(RoomId.New(), new("Room"), new("csharp"), new(""), new(1), DateTimeOffset.UnixEpoch, new("admin"));
        var connection = new ConnectionId("connection");
        room.AddUser(connection, new("Alice"), 5);
        _ = room.Append(connection, [1, 2], DateTimeOffset.UnixEpoch);
        Assert.Equal(2, bytes);
        Assert.Equal(1, count);
        Assert.True(room.Checkpoint(connection, new(1), [3], new(""), DateTimeOffset.UnixEpoch));
        Assert.Equal(0, bytes);
        Assert.Equal(0, count);
        _ = room.Append(connection, [4], DateTimeOffset.UnixEpoch);
        room.Close();
        room.Close();
        Assert.Equal(0, bytes);
        Assert.Equal(0, count);
    }
}

public sealed class Base64SizeTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    public void EncodedLengthAccountsForPadding(int bytes, long characters)
    {
        Assert.Equal(characters, Base64Size.EncodedLength(bytes));
    }
}

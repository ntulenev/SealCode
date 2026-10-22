using Models.Exceptions;

namespace Models.Tests;

public sealed class RoomStateConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentJoinsPreserveCapacityAndUniqueNames(bool sameName)
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var room = Room();
            await Concurrency.RunTogether(Enumerable.Range(0, 8).Select<int, Action>(index => () =>
            {
                try
                {
                    room.AddUser(new($"connection-{index}"), new(sameName ? "Alice" : $"User-{index}"), 2);
                }
                catch (AddRoomUserException) { }
            }).ToArray());
            var users = room.CreateUsersSnapshot();
            Assert.Equal(sameName ? 1 : 2, users.Length);
            Assert.Equal(users.Length, users.Select(user => user.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public async Task CloseSerializesAgainstAllMutations()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            for (var operation = 0; operation < 3; operation++)
            {
                var room = Room();
                room.AddUser(new("alice"), new("Alice"), 5);
                var succeeded = false;
                var closed = false;
                bool? checkpointAccepted = null;
                Action mutation = operation switch
                {
                    0 => () => room.Append(new("alice"), [1], DateTimeOffset.UnixEpoch),
                    1 => () => room.AddUser(new("bob"), new("Bob"), 5),
                    _ => () => checkpointAccepted = room.Checkpoint(new("alice"), new(0), [1], new("checkpoint"), DateTimeOffset.UnixEpoch)
                };
                await Concurrency.RunTogether([
                    () =>
                    {
                        try
                        {
                            mutation();
                            succeeded = true;
                        }
                        catch (RoomNotFoundException)
                        {
                            closed = true;
                        }
                    },
                    room.Close
                ]);
                Assert.NotEqual(succeeded, closed);
                Assert.Equal(operation == 0 && succeeded ? 1 : 0, room.DocumentRevision.Value);
                Assert.Equal(operation == 1 && succeeded, room.CreateUsersSnapshot().Any(user => user.Value == "Bob"));
                if (operation == 2)
                {
                    Assert.Equal(succeeded ? true : (bool?)null, checkpointAccepted);
                    Assert.Equal(succeeded ? "checkpoint" : "", room.Text.Value);
                }
                Assert.Equal(0, room.JournalCount);
                Assert.Equal(operation == 2 && succeeded ? new byte[] { 1 } : [], room.YjsState);
                Assert.Throws<RoomNotFoundException>(mutation);
            }
        }
    }

    [Fact]
    public void LeavingOneParticipantCannotExpireAnOccupiedRoom()
    {
        var room = Room();
        room.AddUser(new("alice"), new("Alice"), 5);
        room.AddUser(new("bob"), new("Bob"), 5);
        room.Leave(new("alice"));
        Assert.False(room.TryCloseIfIdle(DateTimeOffset.MaxValue));
        Assert.Equal("Bob", Assert.Single(room.CreateUsersSnapshot()).Value);
    }

    [Fact]
    public async Task ExpirationRacesWithTheLastDepartureAndANewJoin()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var room = Room();
            room.AddUser(new("alice"), new("Alice"), 5);
            var closed = false;
            var bobAdded = false;
            var bobRejected = false;
            await Concurrency.RunTogether([
                () => room.Leave(new("alice")),
                () => closed = room.TryCloseIfIdle(DateTimeOffset.MaxValue),
                () =>
                {
                    try
                    {
                        room.AddUser(new("bob"), new("Bob"), 5);
                        bobAdded = true;
                    }
                    catch (RoomNotFoundException)
                    {
                        bobRejected = true;
                    }
                }
            ]);
            Assert.Equal(!closed, bobAdded);
            Assert.Equal(closed, bobRejected);
            if (closed)
            {
                Assert.Empty(room.CreateUsersSnapshot());
            }
            else
            {
                Assert.Equal("Bob", Assert.Single(room.CreateUsersSnapshot()).Value);
            }
        }
    }

    [Fact]
    public async Task ParallelAppendsAssignContiguousUniqueRevisions()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var room = Room();
            room.AddUser(new("alice"), new("Alice"), 5);
            var revisions = new System.Collections.Concurrent.ConcurrentBag<long>();
            await Concurrency.RunTogether(Enumerable.Range(0, 32).Select<int, Action>(_ => () =>
                revisions.Add(room.Append(new("alice"), [1], DateTimeOffset.UnixEpoch).DocumentRevision.Value)).ToArray());
            Assert.Equal(Enumerable.Range(1, 32).Select(value => (long)value), revisions.Order());
        }
    }

    private static RoomState Room() => new(RoomId.New(), new("Room"), new("csharp"), new(""), new(1), DateTimeOffset.UnixEpoch, new("admin"));

}

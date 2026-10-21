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
            await Together(Enumerable.Range(0, 8).Select<int, Action>(index => () =>
            {
                try
                {
                    room.AddUser(new($"connection-{index}"), new(sameName ? "Alice" : $"User-{index}"), 2);
                }
                catch (AddRoomUserException) { }
            }));
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
                Action mutation = operation switch
                {
                    0 => () => room.Append(new("alice"), [1], DateTimeOffset.UnixEpoch),
                    1 => () => room.AddUser(new("bob"), new("Bob"), 5),
                    _ => () => Assert.True(room.Checkpoint(new("alice"), new(0), [1], new(""), DateTimeOffset.UnixEpoch))
                };
                await Together([
                    () =>
                    {
                        try { mutation(); }
                        catch (RoomNotFoundException) { }
                    },
                    room.Close
                ]);
                Assert.Throws<RoomNotFoundException>(mutation);
            }
        }
    }

    [Fact]
    public async Task LeavingOneParticipantCannotExpireAnOccupiedRoom()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var room = Room();
            room.AddUser(new("alice"), new("Alice"), 5);
            room.AddUser(new("bob"), new("Bob"), 5);
            await Together([
                () => room.Leave(new("alice")),
                () => Assert.False(room.TryCloseIfIdle(DateTimeOffset.MaxValue))
            ]);
            Assert.Equal("Bob", Assert.Single(room.CreateUsersSnapshot()).Value);
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
            await Together(Enumerable.Range(0, 32).Select<int, Action>(_ => () =>
                revisions.Add(room.Append(new("alice"), [1], DateTimeOffset.UnixEpoch).DocumentRevision.Value)));
            Assert.Equal(Enumerable.Range(1, 32).Select(value => (long)value), revisions.Order());
        }
    }

    private static RoomState Room() => new(RoomId.New(), new("Room"), new("csharp"), new(""), new(1), DateTimeOffset.UnixEpoch, new("admin"));

    private static async Task Together(IEnumerable<Action> actions)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = actions.Select(action => Task.Run(async () =>
        {
            await gate.Task;
            action();
        })).ToArray();
        gate.SetResult();
        await Task.WhenAll(tasks);
    }
}

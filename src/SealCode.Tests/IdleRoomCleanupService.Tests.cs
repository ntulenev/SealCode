using Abstractions;

using Logic;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Models;
using Models.Configuration;

using Moq;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class IdleRoomCleanupServiceTests
{
    private static IOptions<ApplicationConfiguration> Settings(int minutes) => Options.Create(new ApplicationConfiguration
    {
        AdminUsers = [],
        Languages = ["csharp"],
        MaxUsersPerRoom = 5,
        Limits = new RoomLimits { RoomIdleTimeoutMinutes = minutes }
    });

    private static RoomRegistry Registry(FakeTimeProvider clock, int minutes = 5)
        => new(NullLogger<RoomRegistry>.Instance, Settings(minutes), clock);

    private static IdleRoomCleanupService Service(IRoomRegistry registry, FakeTimeProvider clock, int minutes = 5)
        => new(registry, Settings(minutes), clock, NullLogger<IdleRoomCleanupService>.Instance);

    private static RoomState Room(RoomRegistry registry)
        => registry.CreateRoom(new("Room"), new("csharp"), new("Admin"));

    [Fact]
    public async Task EmptyRoomExpiresAtTimeoutAndKeepsUpdatedTimestampMeaning()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var registry = Registry(clock);
        var room = Room(registry);
        using var service = Service(registry, clock);
        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));

        await service.CleanupAsync(CancellationToken.None);
        Assert.True(registry.TryGetRoom(room.RoomId, out _));
        clock.Advance(TimeSpan.FromTicks(1));
        await service.CleanupAsync(CancellationToken.None);

        Assert.False(registry.TryGetRoom(room.RoomId, out _));
        Assert.Equal(DateTimeOffset.UnixEpoch, room.LastUpdatedUtc);
    }

    [Fact]
    public async Task ConnectedRoomNeverExpires()
    {
        var clock = new FakeTimeProvider();
        var registry = Registry(clock);
        var room = Room(registry);
        room.AddUser(new("connection"), new("Alice"), 5);
        clock.Advance(TimeSpan.FromDays(365));
        using var service = Service(registry, clock);

        await service.CleanupAsync(CancellationToken.None);

        Assert.True(registry.TryGetRoom(room.RoomId, out _));
    }

    [Fact]
    public async Task JoinResetsActivityAndLeaveRestartsTheEmptyRoomTimeout()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var registry = Registry(clock);
        var room = Room(registry);
        clock.Advance(TimeSpan.FromMinutes(4));
        room.AddUser(new("connection"), new("Alice"), 5);
        Assert.Equal(clock.GetUtcNow(), room.LastActivityUtc);
        Assert.Equal(DateTimeOffset.UnixEpoch, room.LastUpdatedUtc);
        clock.Advance(TimeSpan.FromMinutes(2));
        _ = room.Leave(new("connection"));
        Assert.Equal(clock.GetUtcNow(), room.LastActivityUtc);
        using var service = Service(registry, clock);
        clock.Advance(TimeSpan.FromMinutes(4));

        await service.CleanupAsync(CancellationToken.None);
        Assert.True(registry.TryGetRoom(room.RoomId, out _));
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.CleanupAsync(CancellationToken.None);
        Assert.False(registry.TryGetRoom(room.RoomId, out _));
    }

    [Fact]
    public async Task DisabledCleanupDoesNotQueryOrDeleteRooms()
    {
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        using var service = Service(registry.Object, new FakeTimeProvider(), minutes: 0);

        await service.CleanupAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ParticipantJoiningAfterSelectionWinsAgainstExpiration()
    {
        var clock = new FakeTimeProvider();
        var registry = Registry(clock);
        var room = Room(registry);
        clock.Advance(TimeSpan.FromMinutes(6));
        var racingRegistry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        racingRegistry.Setup(value => value.GetRoomsSnapshot()).Returns(registry.GetRoomsSnapshot);
        racingRegistry.Setup(value => value.DeleteRoomAsync(room.RoomId,
                It.Is<RoomDeletionReason>(reason => reason.Value == "Room expired"), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback(() => room.AddUser(new("connection"), new("Alice"), 5))
            .Returns((RoomId id, RoomDeletionReason reason, DateTimeOffset cutoff, CancellationToken token) => registry.DeleteRoomAsync(id, reason, cutoff, token));
        using var service = Service(racingRegistry.Object, clock);

        await service.CleanupAsync(CancellationToken.None);

        Assert.True(registry.TryGetRoom(room.RoomId, out _));
        Assert.Equal(1, room.ConnectedUserCount);
        racingRegistry.VerifyAll();
    }

    [Fact]
    public void EveryDocumentAndMetadataMutationUpdatesActivityFromTheInjectedClock()
    {
        var clock = new FakeTimeProvider();
        var registry = Registry(clock);
        var room = Room(registry);
        var connection = new ConnectionId("connection");
        room.AddUser(connection, new("Alice"), 5);
        clock.Advance(TimeSpan.FromMinutes(1));
        _ = room.Append(connection, [1], DateTimeOffset.UnixEpoch);
        Assert.Equal(clock.GetUtcNow(), room.LastActivityUtc);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(room.Checkpoint(connection, new(1), [2], new("text"), DateTimeOffset.UnixEpoch));
        Assert.Equal(clock.GetUtcNow(), room.LastActivityUtc);
        clock.Advance(TimeSpan.FromMinutes(1));
        var languages = new Mock<ILanguageValidator>();
        languages.Setup(value => value.IsValid(It.IsAny<RoomLanguage>())).Returns(true);
        _ = room.ChangeLanguage(connection, new("sql"), DateTimeOffset.UnixEpoch, languages.Object);
        Assert.Equal(clock.GetUtcNow(), room.LastActivityUtc);
    }
}

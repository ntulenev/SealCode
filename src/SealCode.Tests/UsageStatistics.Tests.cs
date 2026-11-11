using Abstractions;

using Microsoft.Extensions.Configuration;

using Models;

using Moq;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class UsageStatisticsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void StatisticsNeverFallBackToSnippetConnection(string? connection)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Snippets"] = "Host=snippet-only;Database=snippets",
            ["ConnectionStrings:Statistics"] = connection
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new UsageStatisticsDataSource(configuration));
        Assert.Contains("ConnectionStrings:Statistics", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DedicatedPoolUsesOnlyTheStatisticsConnectionAndDoesNotConnectDuringConstruction()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Snippets"] = "not a connection string",
            ["ConnectionStrings:Statistics"] = "Host=statistics-only;Database=usage"
        }).Build();
        using var pool = new UsageStatisticsDataSource(configuration);
        Assert.Contains("Host=statistics-only", pool.Value.ConnectionString, StringComparison.Ordinal);
        Assert.Contains("Database=usage", pool.Value.ConnectionString, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => new UsageStatisticsDataSource(null!));
    }

    [Theory]
    [InlineData(RoomKind.Code)]
    [InlineData(RoomKind.Whiteboard)]
    public async Task CreationRecordsTheExactRoomAndDoesNotRollBackSuccessfulWrites(RoomKind kind)
    {
        var room = Room(kind);
        var rooms = new Mock<IRoomManager>();
        var registry = new Mock<IRoomRegistry>(MockBehavior.Strict);
        var statistics = new Mock<IUsageStatisticsRepository>();
        using var cancellation = new CancellationTokenSource();
        rooms.Setup(value => value.CreateRoom(room.Name, room.Language, room.CreatedBy, kind)).Returns(room);
        statistics.Setup(value => value.RecordRoomAsync(room, cancellation.Token)).Returns(Task.CompletedTask);
        var creator = new TrackedRoomCreator(rooms.Object, registry.Object, statistics.Object);

        Assert.Same(room, await creator.CreateAsync(room.Name, room.Language, room.CreatedBy, kind, cancellation.Token));
        statistics.Verify(value => value.RecordRoomAsync(room, cancellation.Token), Times.Once);
        registry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCanceledWritesRollBackWithoutUsingTheCanceledRequestToken(bool canceled)
    {
        var room = Room(RoomKind.Code);
        var rooms = new Mock<IRoomManager>();
        var registry = new Mock<IRoomRegistry>();
        var statistics = new Mock<IUsageStatisticsRepository>();
        rooms.Setup(value => value.CreateRoom(room.Name, room.Language, room.CreatedBy, room.Kind)).Returns(room);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Exception failure = canceled ? new OperationCanceledException(cancellation.Token) : new InvalidOperationException("Write failed");
        statistics.Setup(value => value.RecordRoomAsync(room, cancellation.Token)).ThrowsAsync(failure);
        registry.Setup(value => value.DeleteRoomAsync(room.RoomId, It.IsAny<RoomDeletionReason>(), CancellationToken.None)).ReturnsAsync(true);
        var creator = new TrackedRoomCreator(rooms.Object, registry.Object, statistics.Object);

        var actual = await Record.ExceptionAsync(() => creator.CreateAsync(room.Name, room.Language, room.CreatedBy, room.Kind, cancellation.Token));
        Assert.Same(failure, actual);
        registry.Verify(value => value.DeleteRoomAsync(room.RoomId, It.IsAny<RoomDeletionReason>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public void CreatorRejectsMissingDependencies()
    {
        var rooms = Mock.Of<IRoomManager>();
        var registry = Mock.Of<IRoomRegistry>();
        var statistics = Mock.Of<IUsageStatisticsRepository>();
        Assert.Throws<ArgumentNullException>(() => new TrackedRoomCreator(null!, registry, statistics));
        Assert.Throws<ArgumentNullException>(() => new TrackedRoomCreator(rooms, null!, statistics));
        Assert.Throws<ArgumentNullException>(() => new TrackedRoomCreator(rooms, registry, null!));
    }

    private static RoomState Room(RoomKind kind)
        => new(RoomId.New(), new RoomName("Tracked room"), new RoomLanguage(kind == RoomKind.Code ? "csharp" : "plaintext"),
            new RoomText(""), new RoomVersion(1), DateTimeOffset.UtcNow, new AdminUser("Admin"), kind: kind);
}

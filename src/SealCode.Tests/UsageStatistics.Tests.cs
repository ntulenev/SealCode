using Abstractions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

using Moq;

using Npgsql;

using Persistence.Snippets;
using Persistence.Statistics;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class UsageStatisticsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingSharedConnectionDisablesAccountingWithoutResolvingAnyDatabase(string? connection)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SealCode"] = connection
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSealCodeStatistics();
        using var provider = services.BuildServiceProvider();
        Assert.IsType<DisabledUsageStatisticsRepository>(provider.GetRequiredService<IUsageStatisticsRepository>());
        Assert.False(provider.GetRequiredService<IUsageStatisticsRepository>().Enabled);
    }

    [Fact]
    public async Task DisabledAccountingDoesNotStoreEventsAndRejectsReportOperations()
    {
        var statistics = new DisabledUsageStatisticsRepository();
        await statistics.RecordLoginAsync(new AdminUser("Admin"), "password", CancellationToken.None);
        await statistics.RecordRoomAsync(Room(RoomKind.Code), CancellationToken.None);
        Assert.False(statistics.Enabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => statistics.ReadAsync(new UsageStatisticsQuery(), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => statistics.DeleteBeforeAsync(DateTimeOffset.UtcNow, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => statistics.RecordRoomAsync(null!, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => statistics.RecordLoginAsync(new AdminUser("Admin"), "password", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => statistics.RecordRoomAsync(Room(RoomKind.Code), cancellation.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingSharedConnectionReportsTheNewSetting(string? connection)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:SealCode"] = connection }).Build());
        services.AddSealCodeStatistics();
        using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<InvalidOperationException>(provider.GetRequiredService<NpgsqlDataSource>);
        Assert.Contains("ConnectionStrings:SealCode", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothFeaturesShareOneLazyPoolRegardlessOfRegistrationOrder(bool snippetsFirst)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:SealCode"] = "Host=localhost;Database=sealcode" }).Build());
        services.AddSingleton(Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = ["csharp"],
            Snippets = new() { Enabled = true, Provider = "PostgreSql" }
        }));
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.ContentRootPath).Returns(Path.GetTempPath());
        if (snippetsFirst)
        {
            services.AddSealCodeSnippets(environment.Object);
            services.AddSealCodeStatistics();
        }
        else
        {
            services.AddSealCodeStatistics();
            services.AddSealCodeSnippets(environment.Object);
        }
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(NpgsqlDataSource));
        using var provider = services.BuildServiceProvider();
        Assert.IsType<PostgreSqlSnippetRepository>(provider.GetRequiredService<ISnippetRepository>());
        Assert.IsType<PostgreSqlUsageStatisticsRepository>(provider.GetRequiredService<IUsageStatisticsRepository>());
        var pool = provider.GetRequiredService<NpgsqlDataSource>();
        Assert.Contains("Database=sealcode", pool.ConnectionString, StringComparison.Ordinal);
        Assert.Same(pool, provider.GetRequiredService<NpgsqlDataSource>());
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

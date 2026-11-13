using System.Net.Sockets;

using Abstractions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Models.Configuration;

using Moq;

using Npgsql;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class SnippetCatalogStartupServiceTests
{
    [Fact]
    public async Task MissingDatabaseKeyIsNamedWithoutPreventingStartup()
    {
        using var provider = new AuditLogProvider();
        using var logs = LoggerFactory.Create(builder =>
            builder.AddProvider(provider));
        var catalog = new Mock<ISnippetCatalog>(MockBehavior.Strict);
        catalog.Setup(value => value.InitializeAsync(CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("private-message"));
        var service = new SnippetCatalogStartupService(catalog.Object,
            Settings("PostgreSql", 0, 0), TimeProvider.System,
            logs.CreateLogger<SnippetCatalogStartupService>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        await service.StartAsync(CancellationToken.None);
        Assert.Contains(provider.Entries, entry =>
            entry.Level == LogLevel.Warning
            && entry.Text.Contains("ConnectionStrings:SealCode", StringComparison.Ordinal));
        Assert.All(provider.Entries, entry =>
            Assert.DoesNotContain("private-message", entry.Text, StringComparison.Ordinal));
        catalog.Verify(value => value.InitializeAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task StartupForwardsCancellationAndShutdownDoesNotInitializeAgain()
    {
        using var cancellation = new CancellationTokenSource();
        var catalog = new Mock<ISnippetCatalog>(MockBehavior.Strict);
        catalog.Setup(value => value.InitializeAsync(cancellation.Token)).Returns(Task.CompletedTask);
        var service = Service(catalog.Object);

        await service.StartAsync(cancellation.Token);
        await service.StopAsync(CancellationToken.None);

        catalog.Verify(value => value.InitializeAsync(cancellation.Token), Times.Once);
        catalog.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("PostgreSql", "network", 3)]
    [InlineData("postgresql", "timeout", 3)]
    [InlineData("PostgreSql", "socket", 3)]
    [InlineData("PostgreSql", "57P03", 3)]
    [InlineData("PostgreSql", "53300", 3)]
    [InlineData("PostgreSql", "08006", 3)]
    [InlineData("PostgreSql", "28P01", 1)]
    [InlineData("PostgreSql", "42P01", 1)]
    [InlineData("PostgreSql", "data", 1)]
    [InlineData("PostgreSql", "configuration", 1)]
    [InlineData("File", "network", 1)]
    [InlineData("File", "data", 1)]
    public async Task StartupSurvivesReadFailuresAndRetriesOnlyTemporaryDatabaseFailuresAsync(
        string provider, string failureKind, int attempts)
    {
        Exception failure = failureKind switch
        {
            "network" => new NpgsqlException("Connection failed", new IOException("Disconnected")),
            "timeout" => new TimeoutException("Connection timed out"),
            "socket" => new SocketException((int)SocketError.ConnectionRefused),
            "data" => new InvalidDataException("Invalid catalog"),
            "configuration" => new ArgumentException("Invalid connection configuration"),
            _ => new PostgresException("Server reported a failure", "ERROR", "ERROR", failureKind)
        };
        var catalog = new Mock<ISnippetCatalog>(MockBehavior.Strict);
        catalog.Setup(value => value.InitializeAsync(CancellationToken.None)).ThrowsAsync(failure);

        await Service(catalog.Object, provider).StartAsync(CancellationToken.None);

        catalog.Verify(value => value.InitializeAsync(CancellationToken.None), Times.Exactly(attempts));
        catalog.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RetryDelayUsesConfiguredClockAndStopsAfterSuccessAsync()
    {
        var clock = new FakeTimeProvider();
        var catalog = new Mock<ISnippetCatalog>(MockBehavior.Strict);
        catalog.SetupSequence(value => value.InitializeAsync(CancellationToken.None))
            .ThrowsAsync(new TimeoutException("Temporary connection failure"))
            .Returns(Task.CompletedTask);
        var service = Service(catalog.Object, "PostgreSql", clock, delay: 2);
        var startup = service.StartAsync(CancellationToken.None);
        Assert.False(startup.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        catalog.Verify(value => value.InitializeAsync(CancellationToken.None), Times.Once);
        clock.Advance(TimeSpan.FromSeconds(1));
        await startup.WaitAsync(TimeSpan.FromSeconds(5));
        catalog.Verify(value => value.InitializeAsync(CancellationToken.None), Times.Exactly(2));
    }

    [Fact]
    public async Task CancellationInterruptsRetryDelayWithoutAnotherAttemptAsync()
    {
        var clock = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var catalog = new Mock<ISnippetCatalog>(MockBehavior.Strict);
        catalog.Setup(value => value.InitializeAsync(cancellation.Token)).ThrowsAsync(new TimeoutException());
        var startup = Service(catalog.Object, "PostgreSql", clock, delay: 60).StartAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);
        catalog.Verify(value => value.InitializeAsync(cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task ZeroRetriesMakesOneAttemptAsync()
    {
        var catalog = new Mock<ISnippetCatalog>(MockBehavior.Strict);
        catalog.Setup(value => value.InitializeAsync(CancellationToken.None)).ThrowsAsync(new TimeoutException());
        await Service(catalog.Object, "PostgreSql", retries: 0).StartAsync(CancellationToken.None);
        catalog.Verify(value => value.InitializeAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var catalog = new Mock<ISnippetCatalog>().Object;
        Assert.Throws<ArgumentNullException>(() => Service(null!));
        Assert.Throws<ArgumentNullException>(() => new SnippetCatalogStartupService(catalog,
            null!, TimeProvider.System, NullLogger<SnippetCatalogStartupService>.Instance, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
        Assert.Throws<ArgumentNullException>(() => new SnippetCatalogStartupService(catalog,
            Settings("File", 0, 0), null!, NullLogger<SnippetCatalogStartupService>.Instance, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
        Assert.Throws<ArgumentNullException>(() => new SnippetCatalogStartupService(catalog,
            Settings("File", 0, 0), TimeProvider.System, null!, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
    }

    private static SnippetCatalogStartupService Service(ISnippetCatalog catalog, string provider = "File",
        TimeProvider? clock = null, int delay = 0, int retries = 2)
        => new(catalog, Settings(provider, delay, retries), clock ?? TimeProvider.System,
            NullLogger<SnippetCatalogStartupService>.Instance, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

    private static IOptions<ApplicationConfiguration> Settings(string provider, int delay, int retries)
        => Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = ["csharp"],
            Snippets = new() { Enabled = true, Provider = provider, StartupRetryCount = retries, StartupRetryDelaySeconds = delay }
        });
}

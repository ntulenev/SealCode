using System.Threading.Channels;

using Abstractions;

using Logic;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Models.Configuration;
using Models.Snippets;

using Moq;

using Npgsql;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class SnippetCatalogRefreshServiceTests
{
    [Theory]
    [InlineData(nameof(NpgsqlException))]
    [InlineData(nameof(TimeoutException))]
    [InlineData(nameof(InvalidDataException))]
    [InlineData(nameof(OperationCanceledException))]
    [InlineData(nameof(InvalidOperationException))]
    public async Task FailedDatabaseChecksKeepHostRunningAndPreservePublishedCatalogAsync(string failureType)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var observed = new ObservedClock(clock);
        var version = DateTimeOffset.UnixEpoch;
        var repository = new Mock<IVersionedSnippetRepository>(MockBehavior.Strict);
        var library = new SnippetLibrary([
            new(new("general"), "General", [new(1, "Topic", "Coverage", "Original question")])
        ]);
        repository.Setup(value => value.LoadIfChangedAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VersionedSnippetLibrary(version, library));
        Exception failure = failureType switch
        {
            nameof(NpgsqlException) => new NpgsqlException("Database unavailable"),
            nameof(TimeoutException) => new TimeoutException("Read timed out"),
            nameof(InvalidDataException) => new InvalidDataException("Invalid persisted catalog"),
            nameof(OperationCanceledException) => new OperationCanceledException("Read canceled"),
            _ => new InvalidOperationException("Unexpected read failure")
        };
        repository.Setup(value => value.LoadIfChangedAsync(version, It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        var warnings = Channel.CreateUnbounded<bool>();
        var logger = new WarningLogger(warnings.Writer);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
        builder.Services.AddSingleton(Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = ["csharp"],
            Snippets = new() { Enabled = true, Provider = "PostgreSql" }
        }));
        builder.Services.AddSingleton<TimeProvider>(observed);
        builder.Services.AddSingleton<ISnippetRepository>(repository.Object);
        builder.Services.AddSingleton<ISnippetCatalog, SnippetCatalog>();
        builder.Services.AddSingleton<ILogger<SnippetCatalogRefreshService>>(logger);
        builder.Services.AddHostedService<SnippetCatalogStartupService>();
        builder.Services.AddHostedService<SnippetCatalogRefreshService>();
        using var host = builder.Build();
        try
        {
            await host.StartAsync();
            await observed.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var catalog = host.Services.GetRequiredService<ISnippetCatalog>();
            var worker = host.Services.GetServices<IHostedService>().OfType<SnippetCatalogRefreshService>().Single();
            var initial = catalog.GetCatalogSnapshot(new("Root", true));
            Assert.Equal(DateTimeOffset.UnixEpoch, initial.LastSyncedAt);

            for (var attempt = 0; attempt < 2; attempt++)
            {
                clock.Advance(TimeSpan.FromMinutes(30));
                await warnings.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

                Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
                Assert.False(worker.ExecuteTask!.IsCompleted);
                Assert.Same(initial, catalog.GetCatalogSnapshot(new("Root", true)));
                Assert.Equal("Original question", catalog.GetSnapshot(new("Root", true)).Categories[0].Questions[0].Markdown);
            }
            repository.Verify(value => value.LoadIfChangedAsync(version, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
        finally
        {
            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var catalog = new Mock<ISnippetCatalog>().Object;
        Assert.Throws<ArgumentNullException>(() => new SnippetCatalogRefreshService(
            null!, TimeProvider.System, NullLogger<SnippetCatalogRefreshService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new SnippetCatalogRefreshService(
            catalog, null!, NullLogger<SnippetCatalogRefreshService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new SnippetCatalogRefreshService(
            catalog, TimeProvider.System, null!));
    }

    [Fact]
    public async Task ShutdownCancelsAnInProgressRefresh()
    {
        var clock = new FakeTimeProvider();
        var observed = new ObservedClock(clock);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new Mock<ISnippetCatalog>(MockBehavior.Strict);
        catalog.Setup(value => value.RefreshAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken token) =>
            {
                started.TrySetResult(token);
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
        using var service = new SnippetCatalogRefreshService(catalog.Object, observed,
            NullLogger<SnippetCatalogRefreshService>.Instance);
        await service.StartAsync(CancellationToken.None);
        await observed.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMinutes(30));
        var refreshToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(refreshToken.IsCancellationRequested);
        await service.ExecuteTask!;
        catalog.Verify(value => value.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChecksEveryThirtyMinutesAndRetriesAfterFailure()
    {
        var clock = new FakeTimeProvider();
        var observed = new ObservedClock(clock);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var catalog = new Mock<ISnippetCatalog>(MockBehavior.Strict);
        catalog.Setup(value => value.RefreshAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    first.TrySetResult();
                    throw new IOException("Database unavailable");
                }
                second.TrySetResult();
                return Task.CompletedTask;
            });
        using var service = new SnippetCatalogRefreshService(catalog.Object, observed,
            NullLogger<SnippetCatalogRefreshService>.Instance);
        await service.StartAsync(CancellationToken.None);
        await observed.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMinutes(29));
        catalog.Verify(value => value.RefreshAsync(It.IsAny<CancellationToken>()), Times.Never);
        clock.Advance(TimeSpan.FromMinutes(1));
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromMinutes(30));
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);
        catalog.Verify(value => value.RefreshAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        catalog.VerifyNoOtherCalls();
    }

    private sealed class WarningLogger(ChannelWriter<bool> warnings) : ILogger<SnippetCatalogRefreshService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                warnings.TryWrite(true);
            }
        }
    }

    private sealed class ObservedClock(FakeTimeProvider clock) : TimeProvider
    {
        internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = clock.CreateTimer(callback, state, dueTime, period);
            TimerCreated.TrySetResult();
            return timer;
        }
    }
}

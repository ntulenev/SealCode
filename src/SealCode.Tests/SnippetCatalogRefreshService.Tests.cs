using Abstractions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Moq;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class SnippetCatalogRefreshServiceTests
{
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

    private sealed class ObservedClock(FakeTimeProvider clock) : TimeProvider
    {
        internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = clock.CreateTimer(callback, state, dueTime, period);
            TimerCreated.TrySetResult();
            return timer;
        }
    }
}

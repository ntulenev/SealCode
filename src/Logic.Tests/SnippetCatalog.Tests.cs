using Abstractions;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Models;
using Models.Configuration;
using Models.Snippets;

using Moq;

namespace Logic.Tests;

public sealed class SnippetCatalogTests
{
    [Fact]
    public async Task DatabaseRefreshRecoversAnUnavailableStartupCatalogWithoutRestartAsync()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var repository = new Mock<IVersionedSnippetRepository>(MockBehavior.Strict);
        repository.SetupSequence(value => value.LoadIfChangedAsync(null, CancellationToken.None))
            .ThrowsAsync(new IOException("Unavailable at startup"))
            .ReturnsAsync(new VersionedSnippetLibrary(DateTimeOffset.UnixEpoch, new([Category("general")])));
        using var catalog = new SnippetCatalog(repository.Object, Settings(), clock);
        await Assert.ThrowsAsync<IOException>(() => catalog.InitializeAsync(CancellationToken.None));
        Assert.False(catalog.Available);
        Assert.True(catalog.CanAccess(new("Root", true)));
        clock.Advance(TimeSpan.FromMinutes(30));

        await catalog.RefreshAsync(CancellationToken.None);

        Assert.True(catalog.Available);
        Assert.Equal(clock.GetUtcNow(), catalog.GetCatalogSnapshot(new("Root", true)).LastSyncedAt);
        Assert.Single(catalog.GetSnapshot(new("Root", true)).Categories);
    }

    [Fact]
    public async Task FileSynchronizationTimeIsTheCompletedInitialLoadAndDoesNotAdvanceOnRefresh()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var repository = new Mock<ISnippetRepository>();
        repository.Setup(value => value.LoadAsync(CancellationToken.None)).Returns(() =>
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            return Task.FromResult(new SnippetLibrary([Category("general")]));
        });
        using var catalog = new SnippetCatalog(repository.Object, Settings(), clock);
        Assert.Throws<InvalidOperationException>(() => catalog.GetCatalogSnapshot(new("Root", true)));
        await catalog.InitializeAsync(CancellationToken.None);
        var initial = catalog.GetCatalogSnapshot(new("Root", true));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(2), initial.LastSyncedAt);
        Assert.Same(catalog.GetSnapshot(new("Root", true)), initial.Library);

        clock.Advance(TimeSpan.FromDays(1));
        await catalog.RefreshAsync(CancellationToken.None);

        Assert.Same(initial, catalog.GetCatalogSnapshot(new("Root", true)));
        Assert.Throws<UnauthorizedAccessException>(() => catalog.GetCatalogSnapshot(new("Admin", false)));
        Assert.ThrowsAny<ArgumentException>(() => catalog.GetCatalogSnapshot(default));
    }

    [Fact]
    public async Task DatabaseSynchronizationTimeAdvancesOnUnchangedChecksAndUsesPortalClock()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddYears(20));
        var version = DateTimeOffset.UnixEpoch;
        var repository = new Mock<IVersionedSnippetRepository>(MockBehavior.Strict);
        repository.Setup(value => value.LoadIfChangedAsync(null, CancellationToken.None))
            .ReturnsAsync(new VersionedSnippetLibrary(version, new([Category("general")])));
        repository.SetupSequence(value => value.LoadIfChangedAsync(version, CancellationToken.None))
            .Returns(() =>
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                return Task.FromResult<VersionedSnippetLibrary?>(null);
            })
            .ReturnsAsync(new VersionedSnippetLibrary(version.AddMinutes(1), SnippetLibrary.Empty));
        using var catalog = new SnippetCatalog(repository.Object, Settings(), clock);
        await catalog.InitializeAsync(CancellationToken.None);
        var initial = catalog.GetCatalogSnapshot(new("Root", true));
        Assert.Equal(clock.GetUtcNow(), initial.LastSyncedAt);

        clock.Advance(TimeSpan.FromMinutes(30));
        await catalog.RefreshAsync(CancellationToken.None);
        var unchanged = catalog.GetCatalogSnapshot(new("Root", true));
        Assert.Equal(initial.LastSyncedAt.AddMinutes(30).AddSeconds(1), unchanged.LastSyncedAt);
        Assert.Same(initial.Library, unchanged.Library);

        clock.Advance(TimeSpan.FromMinutes(30));
        await catalog.RefreshAsync(CancellationToken.None);
        var changed = catalog.GetCatalogSnapshot(new("Root", true));
        Assert.Equal(clock.GetUtcNow(), changed.LastSyncedAt);
        Assert.Empty(changed.Library.Categories);
        Assert.Single(initial.Library.Categories);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCanceledChecksDoNotAdvanceSynchronizationTime(bool cancel)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        using var cancellation = new CancellationTokenSource();
        var version = DateTimeOffset.UnixEpoch;
        var repository = new Mock<IVersionedSnippetRepository>(MockBehavior.Strict);
        repository.Setup(value => value.LoadIfChangedAsync(null, CancellationToken.None))
            .ReturnsAsync(new VersionedSnippetLibrary(version, new([Category("general")])));
        repository.Setup(value => value.LoadIfChangedAsync(version, cancellation.Token))
            .Returns(async () =>
            {
                clock.Advance(TimeSpan.FromMinutes(30));
                if (!cancel)
                {
                    throw new InvalidDataException("Invalid persisted catalog");
                }
                await cancellation.CancelAsync();
                return new VersionedSnippetLibrary(version.AddMinutes(1), SnippetLibrary.Empty);
            });
        using var catalog = new SnippetCatalog(repository.Object, Settings(), clock);
        await catalog.InitializeAsync(CancellationToken.None);
        var initial = catalog.GetCatalogSnapshot(new("Root", true));

        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.RefreshAsync(cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => catalog.RefreshAsync(cancellation.Token));
        }

        Assert.Same(initial, catalog.GetCatalogSnapshot(new("Root", true)));
    }

    [Fact]
    public async Task MissingVersionedStartupSnapshotFailsAndCanBeRetried()
    {
        var repository = new Mock<IVersionedSnippetRepository>(MockBehavior.Strict);
        repository.SetupSequence(value => value.LoadIfChangedAsync(null, CancellationToken.None))
            .ReturnsAsync((VersionedSnippetLibrary?)null)
            .ReturnsAsync(new VersionedSnippetLibrary(DateTimeOffset.UnixEpoch, SnippetLibrary.Empty));
        using var catalog = new SnippetCatalog(repository.Object, Settings(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.InitializeAsync(CancellationToken.None));
        AssertUninitialized(catalog);
        await catalog.InitializeAsync(CancellationToken.None);
        Assert.Empty(catalog.GetSnapshot(new("Root", true)).Categories);
    }

    [Fact]
    public async Task ConcurrentRefreshesSerializeAndUseTheLastPublishedVersion()
    {
        var originalVersion = DateTimeOffset.UnixEpoch;
        var nextVersion = originalVersion.AddMinutes(1);
        var repository = new Mock<IVersionedSnippetRepository>(MockBehavior.Strict);
        repository.Setup(value => value.LoadIfChangedAsync(null, CancellationToken.None))
            .ReturnsAsync(new VersionedSnippetLibrary(originalVersion, new([Category("general")])));
        var completion = new TaskCompletionSource<VersionedSnippetLibrary?>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.Setup(value => value.LoadIfChangedAsync(originalVersion, CancellationToken.None)).Returns(completion.Task);
        repository.Setup(value => value.LoadIfChangedAsync(nextVersion, CancellationToken.None))
            .ReturnsAsync((VersionedSnippetLibrary?)null);
        using var catalog = new SnippetCatalog(repository.Object, Settings(), TimeProvider.System);
        await catalog.InitializeAsync(CancellationToken.None);

        var first = catalog.RefreshAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var canceledWaiter = catalog.RefreshAsync(cancellation.Token);
        var second = catalog.RefreshAsync(CancellationToken.None);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWaiter);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        completion.SetResult(new(nextVersion, SnippetLibrary.Empty));
        await Task.WhenAll(first, second);

        Assert.Empty(catalog.GetSnapshot(new("Root", true)).Categories);
        repository.Verify(value => value.LoadIfChangedAsync(originalVersion, CancellationToken.None), Times.Once);
        repository.Verify(value => value.LoadIfChangedAsync(nextVersion, CancellationToken.None), Times.Once);
        repository.Verify(value => value.LoadIfChangedAsync(It.IsAny<DateTimeOffset?>(), cancellation.Token), Times.Never);
    }

    [Fact]
    public async Task DisabledVersionedCatalogNeverReadsTheDatabase()
    {
        var repository = new Mock<IVersionedSnippetRepository>(MockBehavior.Strict);
        using var catalog = new SnippetCatalog(repository.Object, Settings(enabled: false), TimeProvider.System);
        await catalog.InitializeAsync(CancellationToken.None);
        await catalog.RefreshAsync(CancellationToken.None);
        Assert.False(catalog.Enabled);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RefreshPublishesAtomicallyAndKeepsOldSnapshotsUsable()
    {
        var version = DateTimeOffset.UnixEpoch;
        var repository = new Mock<IVersionedSnippetRepository>(MockBehavior.Strict);
        repository.Setup(value => value.LoadIfChangedAsync(null, CancellationToken.None))
            .ReturnsAsync(new VersionedSnippetLibrary(version, new([Category("general")])));
        var completion = new TaskCompletionSource<VersionedSnippetLibrary?>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.Setup(value => value.LoadIfChangedAsync(version, CancellationToken.None)).Returns(completion.Task);
        using var catalog = new SnippetCatalog(repository.Object, Settings(), TimeProvider.System);
        await catalog.InitializeAsync(CancellationToken.None);
        var previous = catalog.GetSnapshot(new("Root", true));

        var refresh = catalog.RefreshAsync(CancellationToken.None);
        Assert.Same(previous, catalog.GetSnapshot(new("Root", true)));
        completion.SetResult(new(version.AddMinutes(1), new([Category("csharp"), Category("sql")])));
        await refresh;

        Assert.Equal("general", Assert.Single(previous.Categories).Language.Value);
        Assert.Equal("csharp", Assert.Single(catalog.GetSnapshot(new("Root", true)).Categories).Language.Value);
        repository.Verify(value => value.LoadAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FailedAndCanceledRefreshesRetainSnapshotAndRetryTheSameVersion()
    {
        var version = DateTimeOffset.UnixEpoch;
        var repository = new Mock<IVersionedSnippetRepository>(MockBehavior.Strict);
        repository.Setup(value => value.LoadIfChangedAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VersionedSnippetLibrary(version, new([Category("general")])));
        using var catalog = new SnippetCatalog(repository.Object, Settings(), TimeProvider.System);
        await catalog.InitializeAsync(CancellationToken.None);
        var previous = catalog.GetSnapshot(new("Root", true));
        repository.Setup(value => value.LoadIfChangedAsync(version, CancellationToken.None))
            .ThrowsAsync(new IOException("Unavailable"));
        await Assert.ThrowsAsync<IOException>(() => catalog.RefreshAsync(CancellationToken.None));
        Assert.Same(previous, catalog.GetSnapshot(new("Root", true)));

        using var cancellation = new CancellationTokenSource();
        repository.Setup(value => value.LoadIfChangedAsync(version, cancellation.Token))
            .Returns(async () =>
            {
                await cancellation.CancelAsync();
                return new VersionedSnippetLibrary(version.AddMinutes(1), SnippetLibrary.Empty);
            });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.RefreshAsync(cancellation.Token));
        Assert.Same(previous, catalog.GetSnapshot(new("Root", true)));

        repository.Setup(value => value.LoadIfChangedAsync(version, CancellationToken.None))
            .ReturnsAsync((VersionedSnippetLibrary?)null);
        await catalog.RefreshAsync(CancellationToken.None);
        Assert.Same(previous, catalog.GetSnapshot(new("Root", true)));
        repository.Verify(value => value.LoadIfChangedAsync(version, CancellationToken.None), Times.Exactly(2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FileAndDisabledCatalogRefreshDoesNotReadPersistence(bool enabled)
    {
        var repository = new Mock<ISnippetRepository>(MockBehavior.Strict);
        if (enabled)
        {
            repository.Setup(value => value.LoadAsync(CancellationToken.None)).ReturnsAsync(SnippetLibrary.Empty);
        }
        using var catalog = new SnippetCatalog(repository.Object, Settings(enabled: enabled), TimeProvider.System);
        await catalog.RefreshAsync(CancellationToken.None);
        await catalog.InitializeAsync(CancellationToken.None);
        await catalog.RefreshAsync(CancellationToken.None);
        repository.Verify(value => value.LoadAsync(CancellationToken.None), enabled ? Times.Once() : Times.Never());
        repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AvailabilityAndPolicyCanBeReadBeforeInitializationButSnapshotsCannot(bool enabled)
    {
        var repository = new Mock<ISnippetRepository>(MockBehavior.Strict);
        using var catalog = new SnippetCatalog(repository.Object, Settings(enabled: enabled), TimeProvider.System);

        AssertUninitialized(catalog);
        Assert.Equal(enabled, catalog.Enabled);
        Assert.Equal(enabled, catalog.CanAccess(new("Root", true)));
        Assert.ThrowsAny<ArgumentException>(() => catalog.CanAccess(default));
        Assert.Throws<InvalidOperationException>(() => catalog.GetSnapshot(default));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DisabledCatalogNeverCallsPersistence()
    {
        var repository = new Mock<ISnippetRepository>(MockBehavior.Strict);
        using var catalog = new SnippetCatalog(repository.Object, Settings(enabled: false), TimeProvider.System);

        await catalog.InitializeAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.InitializeAsync(CancellationToken.None));

        Assert.False(catalog.Enabled);
        Assert.False(catalog.CanAccess(new("Root", true)));
        Assert.Throws<UnauthorizedAccessException>(() => catalog.GetSnapshot(new("Root", true)));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConcurrentInitializationRejectsTheSecondCallerAndLoadsOnce()
    {
        var completion = new TaskCompletionSource<SnippetLibrary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Mock<ISnippetRepository>();
        repository.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).Returns(completion.Task);
        using var catalog = new SnippetCatalog(repository.Object, Settings(), TimeProvider.System);
        var first = catalog.InitializeAsync(CancellationToken.None);
        var second = catalog.InitializeAsync(CancellationToken.None);
        AssertUninitialized(catalog);
        completion.SetResult(new([Category("general"), Category("sql"), Category("csharp")]));
        await first;
        var repeated = await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Equal("The snippet catalog is already initialized.", repeated.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.InitializeAsync(CancellationToken.None));

        var snapshot = catalog.GetSnapshot(new("Root", true));
        Assert.Equal(["general", "csharp"], snapshot.Categories.Select(category => category.Language.Value));
        Assert.Same(snapshot, catalog.GetSnapshot(new("Root", true)));
        repository.Verify(store => store.LoadAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelingASemaphoreWaiterDoesNotCancelOrRestartInitialization()
    {
        var completion = new TaskCompletionSource<SnippetLibrary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Mock<ISnippetRepository>();
        repository.Setup(store => store.LoadAsync(CancellationToken.None)).Returns(completion.Task);
        using var catalog = new SnippetCatalog(repository.Object, Settings(), TimeProvider.System);
        var first = catalog.InitializeAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiter = catalog.InitializeAsync(cancellation.Token);

        Assert.False(waiter.IsCompleted);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.False(first.IsCompleted);
        completion.SetResult(new([Category("general")]));
        await first;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.InitializeAsync(CancellationToken.None));

        Assert.Single(catalog.GetSnapshot(new("Root", true)).Categories);
        repository.Verify(store => store.LoadAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailedLoadReleasesTheSemaphoreAndAllowsTheNextCallerToInitialize()
    {
        var completion = new TaskCompletionSource<SnippetLibrary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var library = new SnippetLibrary([Category("general")]);
        var repository = new Mock<ISnippetRepository>();
        repository.SetupSequence(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .Returns(completion.Task)
            .ReturnsAsync(library);
        using var catalog = new SnippetCatalog(repository.Object, Settings(), TimeProvider.System);
        var first = catalog.InitializeAsync(CancellationToken.None);
        var second = catalog.InitializeAsync(CancellationToken.None);
        var failure = new InvalidDataException("Invalid persisted library");

        completion.SetException(failure);

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidDataException>(() => first));
        await second;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.InitializeAsync(CancellationToken.None));
        Assert.Single(catalog.GetSnapshot(new("Root", true)).Categories);
        repository.Verify(store => store.LoadAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CanceledLoadDoesNotPublishOrMarkTheCatalogInitialized()
    {
        var completion = new TaskCompletionSource<SnippetLibrary>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var library = new SnippetLibrary([Category("general")]);
        var repository = new Mock<ISnippetRepository>();
        repository.Setup(store => store.LoadAsync(cancellation.Token)).Returns(completion.Task);
        repository.Setup(store => store.LoadAsync(CancellationToken.None)).ReturnsAsync(library);
        using var catalog = new SnippetCatalog(repository.Object, Settings(), TimeProvider.System);
        var first = catalog.InitializeAsync(cancellation.Token);
        await cancellation.CancelAsync();

        // A repository that ignores cancellation must finish before the gate is released.
        Assert.False(first.IsCompleted);
        completion.SetResult(library);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        AssertUninitialized(catalog);

        await catalog.InitializeAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.InitializeAsync(CancellationToken.None));
        Assert.Single(catalog.GetSnapshot(new("Root", true)).Categories);
        repository.Verify(store => store.LoadAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public async Task RolePolicyProtectsBothAccessChecksAndSnapshots(bool superOnly, bool superAdmin, bool allowed)
    {
        var repository = new Mock<ISnippetRepository>();
        repository.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(SnippetLibrary.Empty);
        using var catalog = new SnippetCatalog(repository.Object, Settings(superOnly: superOnly), TimeProvider.System);
        await catalog.InitializeAsync(CancellationToken.None);
        var admin = new AdminUser("Admin", superAdmin);

        Assert.Equal(allowed, catalog.CanAccess(admin));
        if (allowed)
        {
            Assert.Empty(catalog.GetSnapshot(admin).Categories);
        }
        else
        {
            Assert.Throws<UnauthorizedAccessException>(() => catalog.GetSnapshot(admin));
        }
    }

    [Fact]
    public async Task InvalidRepositoryResultDoesNotPublishOrCompleteInitialization()
    {
        var repository = new Mock<ISnippetRepository>();
        repository.SetupSequence(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((SnippetLibrary)null!)
            .ReturnsAsync(SnippetLibrary.Empty);
        using var catalog = new SnippetCatalog(repository.Object, Settings(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.InitializeAsync(CancellationToken.None));
        AssertUninitialized(catalog);
        await catalog.InitializeAsync(CancellationToken.None);

        Assert.True(catalog.Enabled);
        Assert.Empty(catalog.GetSnapshot(new("Root", true)).Categories);
        repository.Verify(store => store.LoadAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task FailedStartupDoesNotPublish()
    {
        var repository = new Mock<ISnippetRepository>();
        repository.Setup(store => store.LoadAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException("Invalid persisted library"));
        using var catalog = new SnippetCatalog(repository.Object, Settings(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidDataException>(() => catalog.InitializeAsync(CancellationToken.None));
        AssertUninitialized(catalog);
        repository.Verify(store => store.LoadAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializationPassesStartupCancellationToPersistence()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new Mock<ISnippetRepository>();
        repository.Setup(store => store.LoadAsync(cancellation.Token))
            .Returns(async (CancellationToken token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return SnippetLibrary.Empty;
            });
        using var catalog = new SnippetCatalog(repository.Object, Settings(), TimeProvider.System);
        var initialize = catalog.InitializeAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialize);
        repository.Verify(store => store.LoadAsync(cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task PublicBoundariesRejectInvalidDependenciesAndDefaultAdmins()
    {
        var repository = new Mock<ISnippetRepository>(MockBehavior.Strict);
        Assert.Throws<ArgumentNullException>(() => new SnippetCatalog(null!, Settings(), TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new SnippetCatalog(repository.Object, null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new SnippetCatalog(repository.Object, Settings(), null!));
        using var catalog = new SnippetCatalog(repository.Object, Settings(enabled: false), TimeProvider.System);
        await catalog.InitializeAsync(CancellationToken.None);
        Assert.ThrowsAny<ArgumentException>(() => catalog.CanAccess(default));
        Assert.ThrowsAny<ArgumentException>(() => catalog.GetSnapshot(default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.InitializeAsync(cancellation.Token));
        repository.VerifyNoOtherCalls();
    }

    private static void AssertUninitialized(SnippetCatalog catalog)
    {
        Assert.False(catalog.Available);
        Assert.Throws<InvalidOperationException>(() => catalog.GetSnapshot(new("Root", true)));
    }

    private static IOptions<ApplicationConfiguration> Settings(bool enabled = true, bool superOnly = true)
        => Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = ["csharp"],
            Snippets = new() { Enabled = enabled, SuperAdminsOnly = superOnly }
        });

    private static SnippetCategory Category(string language)
        => new(new(language), language, [new(1, "Topic", "Coverage", "Question")]);
}

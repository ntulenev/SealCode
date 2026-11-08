using Abstractions;

using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;
using Models.Snippets;

using Moq;

namespace Logic.Tests;

public sealed class SnippetCatalogTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AllReadOperationsRejectAnUninitializedCatalog(bool enabled)
    {
        var repository = new Mock<ISnippetRepository>(MockBehavior.Strict);
        using var catalog = new SnippetCatalog(repository.Object, Settings(enabled: enabled));

        AssertUninitialized(catalog);
        Assert.Throws<InvalidOperationException>(() => catalog.CanAccess(default));
        Assert.Throws<InvalidOperationException>(() => catalog.GetSnapshot(default));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DisabledCatalogNeverCallsPersistence()
    {
        var repository = new Mock<ISnippetRepository>(MockBehavior.Strict);
        using var catalog = new SnippetCatalog(repository.Object, Settings(enabled: false));

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
        using var catalog = new SnippetCatalog(repository.Object, Settings());
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
        using var catalog = new SnippetCatalog(repository.Object, Settings());
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
        using var catalog = new SnippetCatalog(repository.Object, Settings());
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
        using var catalog = new SnippetCatalog(repository.Object, Settings());
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
        using var catalog = new SnippetCatalog(repository.Object, Settings(superOnly: superOnly));
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
        using var catalog = new SnippetCatalog(repository.Object, Settings());

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
        using var catalog = new SnippetCatalog(repository.Object, Settings());

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
        using var catalog = new SnippetCatalog(repository.Object, Settings());
        var initialize = catalog.InitializeAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialize);
        repository.Verify(store => store.LoadAsync(cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task PublicBoundariesRejectInvalidDependenciesAndDefaultAdmins()
    {
        var repository = new Mock<ISnippetRepository>(MockBehavior.Strict);
        Assert.Throws<ArgumentNullException>(() => new SnippetCatalog(null!, Settings()));
        Assert.Throws<ArgumentNullException>(() => new SnippetCatalog(repository.Object, null!));
        using var catalog = new SnippetCatalog(repository.Object, Settings(enabled: false));
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
        Assert.Throws<InvalidOperationException>(() => catalog.Enabled);
        Assert.Throws<InvalidOperationException>(() => catalog.CanAccess(new("Root", true)));
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

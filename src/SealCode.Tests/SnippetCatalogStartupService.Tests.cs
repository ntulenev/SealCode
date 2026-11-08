using Abstractions;

using Moq;

using SealCode.Hosting;

namespace SealCode.Tests;

public sealed class SnippetCatalogStartupServiceTests
{
    [Fact]
    public async Task StartupForwardsCancellationAndShutdownDoesNotInitializeAgain()
    {
        using var cancellation = new CancellationTokenSource();
        var catalog = new Mock<ISnippetCatalog>(MockBehavior.Strict);
        catalog.Setup(value => value.InitializeAsync(cancellation.Token)).Returns(Task.CompletedTask);
        var service = new SnippetCatalogStartupService(catalog.Object);

        await service.StartAsync(cancellation.Token);
        await service.StopAsync(CancellationToken.None);

        catalog.Verify(value => value.InitializeAsync(cancellation.Token), Times.Once);
        catalog.VerifyNoOtherCalls();
    }

    [Fact]
    public void ConstructorRejectsANullCatalog() => Assert.Throws<ArgumentNullException>(() => new SnippetCatalogStartupService(null!));
}

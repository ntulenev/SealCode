using Abstractions;

namespace SealCode.Hosting;

/// <summary>
/// Completes catalog initialization before the application accepts requests.
/// </summary>
internal sealed class SnippetCatalogStartupService : IHostedService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnippetCatalogStartupService"/> class.
    /// </summary>
    /// <param name="catalog">The application snippet catalog.</param>
    /// <exception cref="ArgumentNullException">Thrown when the catalog is null.</exception>
    public SnippetCatalogStartupService(ISnippetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _catalog = catalog;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
        => _catalog.InitializeAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private readonly ISnippetCatalog _catalog;
}

using Abstractions;

namespace SealCode.Hosting;

/// <summary>Checks for snippet changes every thirty minutes after startup.</summary>
internal sealed class SnippetCatalogRefreshService : BackgroundService
{
    /// <summary>Creates the refresh worker.</summary>
    /// <param name="catalog">The initialized singleton catalog.</param>
    /// <param name="timeProvider">The scheduling clock.</param>
    /// <param name="logger">The refresh logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public SnippetCatalogRefreshService(ISnippetCatalog catalog, TimeProvider timeProvider,
        ILogger<SnippetCatalogRefreshService> logger)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _catalog = catalog;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30), _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _catalog.RefreshAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // Do not log database exception details, connection strings or authored content.
                    _logger.LogWarning("Snippet catalog refresh failed ({FailureType}); retaining the previous snapshot until the next check.",
                        exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private readonly ISnippetCatalog _catalog;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SnippetCatalogRefreshService> _logger;
}

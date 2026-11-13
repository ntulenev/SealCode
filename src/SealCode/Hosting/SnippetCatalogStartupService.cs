using System.Net.Sockets;

using Abstractions;

using Microsoft.Extensions.Options;

using Models.Configuration;

using Npgsql;

namespace SealCode.Hosting;

/// <summary>
/// Attempts catalog initialization without preventing the application from starting on read failures.
/// </summary>
internal sealed class SnippetCatalogStartupService : IHostedService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnippetCatalogStartupService"/> class.
    /// </summary>
    /// <param name="catalog">The application snippet catalog.</param>
    /// <param name="settings">The validated application settings.</param>
    /// <param name="timeProvider">The retry scheduling clock.</param>
    /// <param name="logger">The startup diagnostics logger.</param>
    /// <param name="configuration">The connection configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public SnippetCatalogStartupService(ISnippetCatalog catalog,
        IOptions<ApplicationConfiguration> settings, TimeProvider timeProvider,
        ILogger<SnippetCatalogStartupService> logger, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        _catalog = catalog;
        _settings = settings.Value.Snippets;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_settings.Enabled
            && _settings.Provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(_configuration.GetConnectionString("SealCode")))
        {
            _logger.LogWarning("Snippet catalog requires ConnectionStrings:SealCode for the PostgreSql provider.");
        }
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _catalog.InitializeAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                var retry = _settings.Provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase)
                    && attempt < _settings.StartupRetryCount && IsConnectionFailure(exception);
                _logger.LogWarning("Snippet catalog startup read failed ({FailureType}); retry scheduled: {Retry}.",
                    exception.GetType().Name, retry);
                if (!retry)
                {
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(_settings.StartupRetryDelaySeconds),
                    _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private static bool IsConnectionFailure(Exception exception) => exception switch
    {
        PostgresException postgres => postgres.SqlState.StartsWith("08", StringComparison.Ordinal)
            || postgres.SqlState is PostgresErrorCodes.TooManyConnections
                or PostgresErrorCodes.AdminShutdown or PostgresErrorCodes.CrashShutdown
                or PostgresErrorCodes.CannotConnectNow,
        NpgsqlException postgres => postgres.IsTransient,
        TimeoutException or SocketException => true,
        _ => false
    };

    private readonly IConfiguration _configuration;
    private readonly ISnippetCatalog _catalog;
    private readonly SnippetConfiguration _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SnippetCatalogStartupService> _logger;
}

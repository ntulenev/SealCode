using Abstractions;

using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;
using Models.Snippets;

namespace Logic;

/// <summary>
/// Applies role and language policy to atomically published immutable snapshots.
/// </summary>
public sealed class SnippetCatalog : ISnippetCatalog, IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnippetCatalog"/> class.
    /// </summary>
    /// <param name="repository">The persistence provider.</param>
    /// <param name="settings">The validated application settings.</param>
    /// <param name="timeProvider">The portal synchronization clock.</param>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public SnippetCatalog(
        ISnippetRepository repository,
        IOptions<ApplicationConfiguration> settings,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var configuration = settings.Value;
        _repository = repository;
        _timeProvider = timeProvider;
        Enabled = configuration.Snippets.Enabled;
        _superAdminsOnly = configuration.Snippets.SuperAdminsOnly;
        _languages = [.. configuration.Languages.Select(language => new RoomLanguage(language))];
    }

    /// <inheritdoc />
    public bool Enabled { get; }

    /// <inheritdoc />
    public bool Available => Enabled && Volatile.Read(ref _initialized);

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                throw new InvalidOperationException("The snippet catalog is already initialized.");
            }

            await LoadAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _initialized, true);
        }
        finally
        {
            _ = _initializationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!Enabled || _repository is not IVersionedSnippetRepository)
        {
            return;
        }
        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _initialized, true);
        }
        finally
        {
            _ = _initializationGate.Release();
        }
    }

    /// <inheritdoc />
    public bool CanAccess(AdminUser adminUser)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminUser.Name, nameof(adminUser));

        return Enabled && (!_superAdminsOnly || adminUser.IsSuperAdmin);
    }

    /// <inheritdoc />
    public SnippetLibrary GetSnapshot(AdminUser adminUser)
        => GetCatalogSnapshot(adminUser).Library;

    /// <inheritdoc />
    public SnippetCatalogSnapshot GetCatalogSnapshot(AdminUser adminUser)
    {
        EnsureInitialized();
        if (!CanAccess(adminUser))
        {
            throw new UnauthorizedAccessException("The administrator cannot access the snippet library.");
        }
        return Volatile.Read(ref _snapshot)
            ?? throw new InvalidOperationException("The snippet catalog must be initialized during startup.");
    }

    /// <summary>
    /// Releases synchronization resources after initialization callers have completed.
    /// </summary>
    public void Dispose()
        => _initializationGate.Dispose();

    private void EnsureInitialized()
    {
        if (!Volatile.Read(ref _initialized))
        {
            throw new InvalidOperationException("The snippet catalog must be initialized during startup.");
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (Enabled && _repository is IVersionedSnippetRepository versioned)
        {
            var snapshot = await versioned.LoadIfChangedAsync(_updatedAt, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot is null)
            {
                if (_updatedAt is null)
                {
                    throw new InvalidOperationException("The snippet repository returned no startup snapshot.");
                }
                Publish(Volatile.Read(ref _snapshot)!.Library);
                return;
            }
            var filtered = snapshot.Library.ForLanguages(_languages);
            Publish(filtered);
            _updatedAt = snapshot.UpdatedAt;
            return;
        }
        var library = Enabled
            ? await _repository.LoadAsync(cancellationToken).ConfigureAwait(false)
            : SnippetLibrary.Empty;
        if (library is null)
        {
            throw new InvalidOperationException("The snippet repository returned no library.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        Publish(library.ForLanguages(_languages));
    }

    private void Publish(SnippetLibrary library)
        => Volatile.Write(ref _snapshot, new(_timeProvider.GetUtcNow(), library));

    private readonly ISnippetRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly RoomLanguage[] _languages;
    private readonly bool _superAdminsOnly;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;
    private SnippetCatalogSnapshot? _snapshot;
    private DateTimeOffset? _updatedAt;
}

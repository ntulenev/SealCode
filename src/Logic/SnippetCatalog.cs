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
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public SnippetCatalog(
        ISnippetRepository repository,
        IOptions<ApplicationConfiguration> settings)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(settings);

        var configuration = settings.Value;
        _repository = repository;
        _enabled = configuration.Snippets.Enabled;
        _superAdminsOnly = configuration.Snippets.SuperAdminsOnly;
        _languages = [.. configuration.Languages.Select(language => new RoomLanguage(language))];
    }

    /// <inheritdoc />
    public bool Enabled
    {
        get
        {
            EnsureInitialized();
            return _enabled;
        }
    }

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
        EnsureInitialized();
        if (!_enabled || _repository is not IVersionedSnippetRepository)
        {
            return;
        }
        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _initializationGate.Release();
        }
    }

    /// <inheritdoc />
    public bool CanAccess(AdminUser adminUser)
    {
        EnsureInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(adminUser.Name, nameof(adminUser));

        return _enabled && (!_superAdminsOnly || adminUser.IsSuperAdmin);
    }

    /// <inheritdoc />
    public SnippetLibrary GetSnapshot(AdminUser adminUser)
    {
        if (!CanAccess(adminUser))
        {
            throw new UnauthorizedAccessException("The administrator cannot access the snippet library.");
        }
        return Volatile.Read(ref _library)
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
        if (_enabled && _repository is IVersionedSnippetRepository versioned)
        {
            var snapshot = await versioned.LoadIfChangedAsync(_updatedAt, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot is null)
            {
                if (_updatedAt is null)
                {
                    throw new InvalidOperationException("The snippet repository returned no startup snapshot.");
                }
                return;
            }
            var filtered = snapshot.Library.ForLanguages(_languages);
            Volatile.Write(ref _library, filtered);
            _updatedAt = snapshot.UpdatedAt;
            return;
        }
        var library = _enabled
            ? await _repository.LoadAsync(cancellationToken).ConfigureAwait(false)
            : SnippetLibrary.Empty;
        if (library is null)
        {
            throw new InvalidOperationException("The snippet repository returned no library.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _library, library.ForLanguages(_languages));
    }

    private readonly ISnippetRepository _repository;
    private readonly RoomLanguage[] _languages;
    private readonly bool _enabled;
    private readonly bool _superAdminsOnly;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;
    private SnippetLibrary? _library;
    private DateTimeOffset? _updatedAt;
}

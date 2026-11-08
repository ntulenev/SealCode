using Abstractions;

using Microsoft.Extensions.Options;

using Models.Configuration;
using Models.Snippets;

using SealCode.Security;

using Transport.Models;

namespace SealCode.Hosting;

/// <summary>Shares snippet edit policy and publication between HTTP and MCP.</summary>
internal sealed class SnippetAdministration
{
    /// <summary>Captures the shared persistence provider, cache and edit configuration.</summary>
    /// <param name="repository">The selected snippet repository.</param>
    /// <param name="catalog">The application catalog refreshed after each commit.</param>
    /// <param name="settings">The validated deployment configuration.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public SnippetAdministration(ISnippetRepository repository, ISnippetCatalog catalog,
        IOptions<ApplicationConfiguration> settings)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(settings);
        _repository = repository;
        _catalog = catalog;
        _settings = settings.Value.Snippets;
    }

    /// <summary>Gets whether this deployment permits database snippet edits.</summary>
    public bool Enabled => _settings.Enabled
        && _settings.Provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase)
        && _repository is IEditableSnippetRepository;

    /// <summary>Checks the current identity independently of read-only snippet access.</summary>
    /// <param name="admins">The current HTTP or MCP identity provider.</param>
    /// <returns>Whether the authenticated caller can edit database snippets.</returns>
    public bool CanEdit(IAdminUserManager admins)
        => Enabled && admins.TryGetAdminUser(out var admin) && admin.IsSuperAdmin;

    /// <summary>Reads an unfiltered database snapshot and its concurrency revision.</summary>
    /// <param name="cancellationToken">Cancels the database read.</param>
    /// <returns>The complete database snapshot.</returns>
    public Task<VersionedSnippetLibrary?> LoadAsync(CancellationToken cancellationToken)
        => ((IEditableSnippetRepository)_repository).LoadIfChangedAsync(null, cancellationToken);

    /// <summary>Commits a question and immediately refreshes the application cache.</summary>
    /// <param name="language">The existing category identifier.</param>
    /// <param name="number">The existing question identifier.</param>
    /// <param name="request">The complete authored revision.</param>
    /// <param name="cancellationToken">Cancels the transaction before commit.</param>
    /// <returns>False when another editor changed the catalog or the question was removed.</returns>
    /// <exception cref="ArgumentException">The authored revision is invalid.</exception>
    public async Task<bool> PublishAsync(string language, int number, UpdateSnippetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var question = request.ToQuestion(number);
        var saved = await ((IEditableSnippetRepository)_repository).UpdateQuestionAsync(
            new SnippetLanguage(language), question, request.ExpectedVersion, cancellationToken).ConfigureAwait(false);
        if (saved)
        {
            // A disconnected editor must not cancel cache publication after the database commit.
            await _catalog.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        }
        return saved;
    }

    private readonly ISnippetRepository _repository;
    private readonly ISnippetCatalog _catalog;
    private readonly SnippetConfiguration _settings;
}

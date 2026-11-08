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
        var original = new SnippetLanguage(language);
        var destination = request.TargetLanguage is null ? original : new SnippetLanguage(request.TargetLanguage);
        var question = request.ToQuestion(request.TargetNumber ?? number);
        var write = original == destination && number == question.Number
            ? Writer.UpdateQuestionAsync(original, question, request.ExpectedVersion, cancellationToken)
            : Writer.RelocateQuestionAsync(original, number, destination, question, request.ExpectedVersion, cancellationToken);
        return await RefreshAfterWriteAsync(write).ConfigureAwait(false);
    }

    /// <summary>Creates or updates category metadata and immediately publishes its new snapshot.</summary>
    /// <param name="request">The metadata, original key and expected revision.</param>
    /// <param name="cancellationToken">Cancels writes before commit.</param>
    /// <returns>False for a missing category, occupied key or stale revision.</returns>
    public Task<bool> SaveCategoryAsync(SaveSnippetCategoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var category = request.ToCategory();
        SnippetLanguage? original = request.OriginalLanguage is null ? null : new(request.OriginalLanguage);
        return RefreshAfterWriteAsync(Writer.SaveCategoryAsync(original, category,
            request.ExpectedVersion, cancellationToken));
    }

    /// <summary>Deletes a category and its complete question tree, then refreshes the cache.</summary>
    /// <param name="language">The existing category key.</param>
    /// <param name="expectedVersion">The observed catalog revision.</param>
    /// <param name="cancellationToken">Cancels writes before commit.</param>
    /// <returns>False for a missing category or stale revision.</returns>
    public Task<bool> DeleteCategoryAsync(string language, DateTimeOffset expectedVersion,
        CancellationToken cancellationToken)
        => RefreshAfterWriteAsync(Writer.DeleteCategoryAsync(new(language), expectedVersion, cancellationToken));

    /// <summary>Creates a complete question in an existing category and immediately refreshes the cache.</summary>
    /// <param name="language">The destination category.</param>
    /// <param name="number">The new question number.</param>
    /// <param name="request">The complete authored question and expected revision.</param>
    /// <param name="cancellationToken">Cancels writes before commit.</param>
    /// <returns>False for a missing category, occupied number or stale revision.</returns>
    public Task<bool> CreateQuestionAsync(string language, int number, UpdateSnippetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TargetLanguage is not null || request.TargetNumber is not null)
        {
            throw new ArgumentException("Use language and number to identify a new question.");
        }
        return RefreshAfterWriteAsync(Writer.CreateQuestionAsync(new(language), request.ToQuestion(number),
            request.ExpectedVersion, cancellationToken));
    }

    /// <summary>Deletes a question and all its examples, then immediately refreshes the cache.</summary>
    /// <param name="language">The existing category.</param>
    /// <param name="number">The existing question number.</param>
    /// <param name="expectedVersion">The observed catalog revision.</param>
    /// <param name="cancellationToken">Cancels writes before commit.</param>
    /// <returns>False for a missing question or stale revision.</returns>
    public Task<bool> DeleteQuestionAsync(string language, int number, DateTimeOffset expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        return RefreshAfterWriteAsync(Writer.DeleteQuestionAsync(new(language), number, expectedVersion, cancellationToken));
    }

    private async Task<bool> RefreshAfterWriteAsync(Task<bool> write)
    {
        var saved = await write.ConfigureAwait(false);
        if (saved)
        {
            // A disconnected editor must not cancel cache publication after the database commit.
            await _catalog.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        }
        return saved;
    }

    private IEditableSnippetRepository Writer => (IEditableSnippetRepository)_repository;

    private readonly ISnippetRepository _repository;
    private readonly ISnippetCatalog _catalog;
    private readonly SnippetConfiguration _settings;
}

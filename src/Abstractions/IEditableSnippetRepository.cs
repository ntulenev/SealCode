using Models.Snippets;

namespace Abstractions;

/// <summary>Persists complete question revisions atomically.</summary>
public interface IEditableSnippetRepository : IVersionedSnippetRepository
{
    /// <summary>Updates an existing question only if the catalog revision still matches.</summary>
    /// <param name="language">The category identifier.</param>
    /// <param name="question">The validated question and all its examples.</param>
    /// <param name="expectedVersion">The revision observed before editing.</param>
    /// <param name="cancellationToken">Cancels the transaction before commit.</param>
    /// <returns>False when the revision changed or the question no longer exists.</returns>
    Task<bool> UpdateQuestionAsync(SnippetLanguage language, SnippetQuestion question,
        DateTimeOffset expectedVersion, CancellationToken cancellationToken);
}

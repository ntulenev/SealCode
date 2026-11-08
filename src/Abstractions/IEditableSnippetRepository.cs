using Models.Snippets;

namespace Abstractions;

/// <summary>Persists category metadata and complete question revisions atomically.</summary>
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

    /// <summary>Creates or updates category metadata, renaming its key and preserving its questions.</summary>
    /// <param name="originalLanguage">The existing key, or null when creating a category.</param>
    /// <param name="category">The validated category metadata; its question list is ignored.</param>
    /// <param name="expectedVersion">The catalog revision observed before editing.</param>
    /// <param name="cancellationToken">Cancels the transaction before commit.</param>
    /// <returns>False for a stale revision, missing source or occupied destination.</returns>
    Task<bool> SaveCategoryAsync(SnippetLanguage? originalLanguage, SnippetCategory category,
        DateTimeOffset expectedVersion, CancellationToken cancellationToken);

    /// <summary>Deletes a category together with its questions and examples.</summary>
    /// <param name="language">The category to delete.</param>
    /// <param name="expectedVersion">The catalog revision observed before editing.</param>
    /// <param name="cancellationToken">Cancels the transaction before commit.</param>
    /// <returns>False for a stale revision, missing source or occupied destination.</returns>
    Task<bool> DeleteCategoryAsync(SnippetLanguage language, DateTimeOffset expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>Creates a question in an existing category without overwriting an occupied number.</summary>
    /// <param name="language">The destination category.</param>
    /// <param name="question">The complete validated question.</param>
    /// <param name="expectedVersion">The catalog revision observed before editing.</param>
    /// <param name="cancellationToken">Cancels the transaction before commit.</param>
    /// <returns>False for a stale revision, missing source or occupied destination.</returns>
    Task<bool> CreateQuestionAsync(SnippetLanguage language, SnippetQuestion question,
        DateTimeOffset expectedVersion, CancellationToken cancellationToken);

    /// <summary>Updates a question, optionally moving it or changing its number, without overwriting a target.</summary>
    /// <param name="originalLanguage">The existing category key.</param>
    /// <param name="originalNumber">The existing question number.</param>
    /// <param name="language">The destination category.</param>
    /// <param name="question">The complete question with its destination number.</param>
    /// <param name="expectedVersion">The catalog revision observed before editing.</param>
    /// <param name="cancellationToken">Cancels the transaction before commit.</param>
    /// <returns>False for a stale revision, missing source or occupied destination.</returns>
    Task<bool> RelocateQuestionAsync(SnippetLanguage originalLanguage, int originalNumber,
        SnippetLanguage language, SnippetQuestion question, DateTimeOffset expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>Deletes a question and all its examples.</summary>
    /// <param name="language">The existing category.</param>
    /// <param name="number">The existing question number.</param>
    /// <param name="expectedVersion">The catalog revision observed before editing.</param>
    /// <param name="cancellationToken">Cancels the transaction before commit.</param>
    /// <returns>False for a stale revision, missing source or occupied destination.</returns>
    Task<bool> DeleteQuestionAsync(SnippetLanguage language, int number, DateTimeOffset expectedVersion,
        CancellationToken cancellationToken);
}

using Models.Snippets;

namespace Transport.Models;

/// <summary>Category metadata and the catalog revision observed before editing.</summary>
/// <param name="ExpectedVersion">The observed database revision.</param>
/// <param name="Language">The destination category key.</param>
/// <param name="Title">The display title.</param>
/// <param name="SortOrder">The display order; language breaks ties.</param>
/// <param name="OriginalLanguage">The existing key, or null to create a new category.</param>
public sealed record SaveSnippetCategoryRequest(DateTimeOffset ExpectedVersion, string Language,
    string Title, int SortOrder = 0, string? OriginalLanguage = null)
{
    /// <summary>Validates metadata without requiring a first question.</summary>
    /// <returns>A category containing validated metadata and no questions.</returns>
    /// <exception cref="ArgumentException">A key or title is invalid.</exception>
    public SnippetCategory ToCategory()
    {
        if (Title is null || Title.Length > 1000)
        {
            throw new ArgumentException("A category title must contain 1–1000 characters.");
        }
        return new(new(Language), Title, [], SortOrder);
    }
}

using System.Collections.ObjectModel;

using Models.Snippets;

namespace Transport.Models;

/// <summary>
/// Represents a language category in the browser API contract.
/// </summary>
public sealed class SnippetCategoryResponse
{
    private SnippetCategoryResponse(SnippetCategory category)
    {
        Language = category.Language.Value;
        Title = category.Title;
        Questions = Array.AsReadOnly(category.Questions.Select(SnippetQuestionResponse.From).ToArray());
    }

    /// <summary>
    /// Gets the Monaco language identifier or general.
    /// </summary>
    public string Language { get; }

    /// <summary>
    /// Gets the category display title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the questions ordered by number.
    /// </summary>
    public ReadOnlyCollection<SnippetQuestionResponse> Questions { get; }

    /// <summary>
    /// Maps a domain category to the HTTP contract without persistence metadata.
    /// </summary>
    /// <param name="category">The validated domain category.</param>
    /// <returns>The response model.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the category is null.</exception>
    public static SnippetCategoryResponse From(SnippetCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return new(category);
    }
}

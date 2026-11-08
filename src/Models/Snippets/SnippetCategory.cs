using System.Collections.ObjectModel;

namespace Models.Snippets;

/// <summary>
/// Owns an ordered set of questions with unique numbers for one language.
/// </summary>
public sealed class SnippetCategory
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnippetCategory"/> class.
    /// </summary>
    /// <param name="language">The category language.</param>
    /// <param name="title">The display title.</param>
    /// <param name="questions">The questions to copy into the category; may be empty.</param>
    /// <param name="sortOrder">The authored category display order.</param>
    /// <exception cref="ArgumentNullException">Thrown when the title or questions are null.</exception>
    /// <exception cref="ArgumentException">Thrown for an invalid language, blank title, null questions or duplicate numbers.</exception>
    public SnippetCategory(
        SnippetLanguage language,
        string title,
        IEnumerable<SnippetQuestion> questions,
        int sortOrder = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language.Value, nameof(language));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(questions);

        var snapshot = questions.ToArray();
        if (snapshot.Any(question => question is null)
            || snapshot.Select(question => question.Number).Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("Category questions must be non-null and have unique numbers.", nameof(questions));
        }

        Language = language;
        Title = title.Trim();
        SortOrder = sortOrder;
        Questions = Array.AsReadOnly(snapshot.OrderBy(question => question.Number).ToArray());
    }

    /// <summary>
    /// Gets the category language.
    /// </summary>
    public SnippetLanguage Language { get; }

    /// <summary>
    /// Gets the display title.
    /// </summary>
    public string Title { get; }

    /// <summary>Gets the authored category display order.</summary>
    public int SortOrder { get; }

    /// <summary>
    /// Gets immutable questions ordered by number.
    /// </summary>
    public ReadOnlyCollection<SnippetQuestion> Questions { get; }
}

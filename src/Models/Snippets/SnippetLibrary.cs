using System.Collections.ObjectModel;

namespace Models.Snippets;

/// <summary>
/// Owns an immutable library whose categories have unique normalized languages.
/// </summary>
public sealed class SnippetLibrary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnippetLibrary"/> class.
    /// </summary>
    /// <param name="categories">The categories to copy into the library.</param>
    /// <exception cref="ArgumentNullException">Thrown when categories are null.</exception>
    /// <exception cref="ArgumentException">Thrown when a category is null or languages repeat.</exception>
    public SnippetLibrary(IEnumerable<SnippetCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        var snapshot = categories.ToArray();
        if (snapshot.Any(category => category is null)
            || snapshot.Select(category => category.Language).Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException("Library categories must be non-null and have unique languages.", nameof(categories));
        }

        Categories = Array.AsReadOnly(snapshot);
    }

    /// <summary>
    /// Gets an immutable empty library.
    /// </summary>
    public static SnippetLibrary Empty { get; } = new([]);

    /// <summary>
    /// Gets immutable categories in their authored order.
    /// </summary>
    public ReadOnlyCollection<SnippetCategory> Categories { get; }

    /// <summary>
    /// Selects configured programming languages while always retaining general and whiteboard questions.
    /// </summary>
    /// <param name="languages">The supported room languages.</param>
    /// <returns>A new immutable library containing eligible categories.</returns>
    /// <exception cref="ArgumentNullException">Thrown when languages are null.</exception>
    /// <exception cref="ArgumentException">Thrown when a language is uninitialized or blank.</exception>
    public SnippetLibrary ForLanguages(IEnumerable<RoomLanguage> languages)
    {
        ArgumentNullException.ThrowIfNull(languages);

        var supported = new HashSet<SnippetLanguage>();
        foreach (var language in languages)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(language.Value, nameof(languages));
            _ = supported.Add(new SnippetLanguage(language.Value));
        }
        return new(Categories.Where(category => category.Language.IsGeneral
            || category.Language.Value == "whiteboard" || supported.Contains(category.Language)));
    }
}

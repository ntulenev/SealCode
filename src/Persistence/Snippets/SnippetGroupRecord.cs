using System.Diagnostics.CodeAnalysis;

namespace Persistence.Snippets;

/// <summary>
/// Describes a language group in the file manifest.
/// </summary>
/// <param name="Language">The language identifier.</param>
/// <param name="Title">The display title.</param>
/// <param name="Questions">The persisted question references.</param>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by System.Text.Json when reading the private file manifest.")]
internal sealed record SnippetGroupRecord(
    string Language,
    string Title,
    SnippetQuestionRecord[] Questions);

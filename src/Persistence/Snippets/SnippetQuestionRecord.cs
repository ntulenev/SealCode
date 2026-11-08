using System.Diagnostics.CodeAnalysis;

namespace Persistence.Snippets;

/// <summary>
/// References a Markdown question in the file manifest.
/// </summary>
/// <param name="Number">The question number within its group.</param>
/// <param name="Topic">The question topic.</param>
/// <param name="Description">The brief explanation of the concepts covered.</param>
/// <param name="File">The direct Markdown filename.</param>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by System.Text.Json when reading the private file manifest.")]
internal sealed record SnippetQuestionRecord(int Number, string Topic, string Description, string File);

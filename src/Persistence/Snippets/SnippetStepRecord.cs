using System.Diagnostics.CodeAnalysis;

namespace Persistence.Snippets;

/// <summary>References one example's private Markdown file.</summary>
/// <param name="Number">The example position.</param>
/// <param name="Title">The example title.</param>
/// <param name="File">The direct Markdown filename.</param>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by System.Text.Json when reading the private file manifest.")]
internal sealed record SnippetStepRecord(int Number, string Title, string File);

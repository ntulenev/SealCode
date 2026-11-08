namespace Models.Snippets;

/// <summary>
/// An immutable example in a question's progression.
/// </summary>
public sealed class SnippetStep
{
    /// <summary>Creates a numbered example while preserving its Markdown indentation.</summary>
    /// <param name="number">The positive position in the progression.</param>
    /// <param name="title">The example's display title.</param>
    /// <param name="markdown">The prompt and fenced code for this example.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the number is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when text is blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when text is null.</exception>
    public SnippetStep(int number, string title, string markdown)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);
        Number = number;
        Title = title.Trim();
        Markdown = markdown;
    }

    /// <summary>Gets the position in the progression.</summary>
    public int Number { get; }

    /// <summary>Gets the display title.</summary>
    public string Title { get; }

    /// <summary>Gets the original Markdown.</summary>
    public string Markdown { get; }
}

namespace Models.Snippets;

/// <summary>
/// Represents an immutable numbered question and its Markdown snippet.
/// </summary>
public sealed class SnippetQuestion
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnippetQuestion"/> class.
    /// </summary>
    /// <param name="number">The positive question number within a category.</param>
    /// <param name="topic">The topic shown in the question list.</param>
    /// <param name="description">The brief explanation of the skills and concepts covered.</param>
    /// <param name="markdown">The question text and fenced code blocks.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the number is not positive.</exception>
    /// <exception cref="ArgumentNullException">Thrown when text is null.</exception>
    /// <exception cref="ArgumentException">Thrown when text is blank.</exception>
    public SnippetQuestion(int number, string topic, string description, string markdown)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        Number = number;
        Topic = topic.Trim();
        Description = description.Trim();
        Markdown = markdown;
    }

    /// <summary>
    /// Gets the question number within its category.
    /// </summary>
    public int Number { get; }

    /// <summary>
    /// Gets the question topic.
    /// </summary>
    public string Topic { get; }

    /// <summary>
    /// Gets the brief explanation of the skills and concepts covered.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the original Markdown, preserving code indentation.
    /// </summary>
    public string Markdown { get; }
}

using System.Collections.ObjectModel;

namespace Models.Snippets;

/// <summary>
/// Represents an immutable numbered question and its ordered examples.
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
        : this(number, topic, description, [new SnippetStep(1, "Original example", markdown)])
    {
    }

    /// <summary>Creates a question with a complete, immutable progression of examples.</summary>
    /// <param name="number">The positive question number within a category.</param>
    /// <param name="topic">The topic shown in the question list.</param>
    /// <param name="description">The concepts covered by the question.</param>
    /// <param name="steps">The examples, numbered consecutively from one.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the question number is not positive.</exception>
    /// <exception cref="ArgumentNullException">Thrown when text or the examples are null.</exception>
    /// <exception cref="ArgumentException">Thrown for blank text, empty examples, null examples or invalid numbering.</exception>
    public SnippetQuestion(int number, string topic, string description, IEnumerable<SnippetStep> steps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(steps);
        var snapshot = steps.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(step => step is null))
        {
            throw new ArgumentException("A question requires non-null examples.", nameof(steps));
        }
        var ordered = snapshot.OrderBy(step => step.Number).ToArray();
        if (!ordered.Select(step => step.Number).SequenceEqual(Enumerable.Range(1, ordered.Length)))
        {
            throw new ArgumentException("Question examples must be numbered consecutively from one.", nameof(steps));
        }

        Number = number;
        Topic = topic.Trim();
        Description = description.Trim();
        Steps = Array.AsReadOnly(ordered);
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
    /// Gets the first example's Markdown for single-example consumers.
    /// </summary>
    public string Markdown => Steps[0].Markdown;

    /// <summary>Gets the immutable examples in progression order.</summary>
    public ReadOnlyCollection<SnippetStep> Steps { get; }
}

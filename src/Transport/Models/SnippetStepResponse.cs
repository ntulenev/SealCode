using Models.Snippets;

namespace Transport.Models;

/// <summary>Represents one example in a question's progression.</summary>
public sealed class SnippetStepResponse
{
    private SnippetStepResponse(SnippetStep step)
    {
        Number = step.Number;
        Title = step.Title;
        Markdown = step.Markdown;
    }

    /// <summary>Gets the example position.</summary>
    public int Number { get; }

    /// <summary>Gets the example title.</summary>
    public string Title { get; }

    /// <summary>Gets the example's prompt and fenced code.</summary>
    public string Markdown { get; }

    /// <summary>Maps a validated example to the browser contract.</summary>
    /// <param name="step">The domain example.</param>
    /// <returns>The response model.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the example is null.</exception>
    public static SnippetStepResponse From(SnippetStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new(step);
    }
}

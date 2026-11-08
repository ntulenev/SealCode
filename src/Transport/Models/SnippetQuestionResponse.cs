using System.Collections.ObjectModel;

using Models.Snippets;

namespace Transport.Models;

/// <summary>
/// Represents an authorized question in the browser API contract.
/// </summary>
public sealed class SnippetQuestionResponse
{
    private SnippetQuestionResponse(SnippetQuestion question)
    {
        Number = question.Number;
        Topic = question.Topic;
        Description = question.Description;
        Markdown = question.Markdown;
        Steps = Array.AsReadOnly(question.Steps.Select(SnippetStepResponse.From).ToArray());
    }

    /// <summary>
    /// Gets the question number within its category.
    /// </summary>
    public int Number { get; }

    /// <summary>
    /// Gets the topic shown in the question list.
    /// </summary>
    public string Topic { get; }

    /// <summary>
    /// Gets the brief explanation of the concepts covered.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the first example's Markdown for single-example consumers.
    /// </summary>
    public string Markdown { get; }

    /// <summary>Gets the examples in progression order.</summary>
    public ReadOnlyCollection<SnippetStepResponse> Steps { get; }

    /// <summary>
    /// Maps a domain question to the HTTP contract.
    /// </summary>
    /// <param name="question">The validated domain question.</param>
    /// <returns>The response model.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the question is null.</exception>
    public static SnippetQuestionResponse From(SnippetQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);

        return new(question);
    }
}

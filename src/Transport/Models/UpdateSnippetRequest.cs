using Models.Snippets;

namespace Transport.Models;

/// <summary>A complete question revision submitted for publication.</summary>
/// <param name="ExpectedVersion">The database revision observed before editing.</param>
/// <param name="Topic">The question topic.</param>
/// <param name="Description">The question description.</param>
/// <param name="Steps">All examples in their publication order.</param>
public sealed record UpdateSnippetRequest(DateTimeOffset ExpectedVersion, string Topic,
    string Description, SnippetStepInput[] Steps)
{
    /// <summary>Validates the complete revision before any database writes.</summary>
    /// <param name="number">The existing question identifier.</param>
    /// <returns>The validated question.</returns>
    /// <exception cref="ArgumentException">The revision exceeds limits or is incomplete.</exception>
    public SnippetQuestion ToQuestion(int number)
    {
        if (Topic is null || Topic.Length > 1000 || Description is null || Description.Length > 10000
            || Steps is null || Steps.Length is < 1 or > 100
            || Steps.Any(step => step is null || step.Title is null || step.Title.Length > 1000
                || step.Markdown is null || step.Markdown.Length > 500000)
            || Steps.Sum(step => (long)step.Markdown.Length) > 2000000)
        {
            throw new ArgumentException("Invalid snippet revision or content limits exceeded.");
        }
        return new(number, Topic, Description, Steps.Select((step, index) =>
            new SnippetStep(index + 1, step.Title, step.Markdown)));
    }
}

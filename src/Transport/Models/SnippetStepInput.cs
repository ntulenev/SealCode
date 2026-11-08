namespace Transport.Models;

/// <summary>An authored example; positions are assigned from array order.</summary>
/// <param name="Title">The display title.</param>
/// <param name="Markdown">The unmodified Markdown source.</param>
public sealed record SnippetStepInput(string Title, string Markdown);

namespace Persistence.Snippets;

/// <summary>A persistence-only joined row, including categories without questions.</summary>
/// <param name="Language">The category language.</param>
/// <param name="CategoryTitle">The category title.</param>
/// <param name="QuestionNumber">The question number, or null for an empty category.</param>
/// <param name="Topic">The question topic.</param>
/// <param name="Description">The question description.</param>
/// <param name="StepNumber">The example position.</param>
/// <param name="StepTitle">The example title.</param>
/// <param name="Markdown">The example Markdown.</param>
/// <param name="SortOrder">The category display order.</param>
internal sealed record PostgreSqlSnippetRow(string Language, string CategoryTitle,
    int? QuestionNumber, string Topic, string Description, int StepNumber,
    string StepTitle, string Markdown, int SortOrder = 0);

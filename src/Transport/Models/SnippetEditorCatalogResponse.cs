namespace Transport.Models;

/// <summary>A database snapshot with the concurrency revision required for publication.</summary>
/// <param name="Version">The database revision shared by all questions.</param>
/// <param name="Categories">All authored categories, including languages disabled for rooms.</param>
public sealed record SnippetEditorCatalogResponse(DateTimeOffset Version, SnippetCategoryResponse[] Categories);

namespace Transport.Models;

/// <summary>
/// Describes whether the current request can open the question library.
/// </summary>
/// <param name="CanAccess">Whether the feature and administrator role allow access.</param>
public sealed record SnippetAccessResponse(bool CanAccess);

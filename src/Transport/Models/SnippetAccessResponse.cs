namespace Transport.Models;

/// <summary>
/// Describes whether the current request can open the question library.
/// </summary>
/// <param name="CanAccess">Whether the feature, administrator role and loaded catalog allow access.</param>
/// <param name="Unavailable">Whether an authorized user should see an unavailable library control.</param>
public sealed record SnippetAccessResponse(bool CanAccess, bool Unavailable = false);

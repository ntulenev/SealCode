namespace Transport.Models;

/// <summary>The observed catalog revision required for a destructive operation.</summary>
/// <param name="ExpectedVersion">The version returned by the editor catalog.</param>
public sealed record SnippetVersionRequest(DateTimeOffset ExpectedVersion);

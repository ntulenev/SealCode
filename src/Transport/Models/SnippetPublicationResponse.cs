namespace Transport.Models;

/// <summary>Confirms a committed revision whose application cache has been refreshed.</summary>
/// <param name="Published">Whether publication completed.</param>
public sealed record SnippetPublicationResponse(bool Published);

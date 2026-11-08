using Models.Snippets;

namespace Abstractions;

/// <summary>
/// A validated immutable library and its database timestamp.
/// </summary>
/// <param name="UpdatedAt">The database timestamp read with the library.</param>
/// <param name="Library">The complete immutable library.</param>
public sealed record VersionedSnippetLibrary(DateTimeOffset UpdatedAt, SnippetLibrary Library);

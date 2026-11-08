using Models.Snippets;

namespace Abstractions;

/// <summary>
/// An immutable catalog and the completion time of its last successful synchronization.
/// </summary>
/// <param name="LastSyncedAt">The portal clock time, including unchanged database checks.</param>
/// <param name="Library">The library available at that synchronization.</param>
public sealed record SnippetCatalogSnapshot(DateTimeOffset LastSyncedAt, SnippetLibrary Library);

namespace Abstractions;

/// <summary>Loads a consistent library only when its persistence timestamp changes.</summary>
public interface IVersionedSnippetRepository : ISnippetRepository
{
    /// <summary>Returns a complete snapshot, or null when the timestamp is unchanged.</summary>
    /// <param name="updatedAt">The timestamp of the last published snapshot, or null for startup.</param>
    /// <param name="cancellationToken">Cancels database reads.</param>
    /// <returns>A consistent timestamp and library, or null when unchanged.</returns>
    Task<VersionedSnippetLibrary?> LoadIfChangedAsync(DateTimeOffset? updatedAt, CancellationToken cancellationToken);
}

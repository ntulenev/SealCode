using Models.Snippets;

namespace Abstractions;

/// <summary>
/// Loads a complete domain library from a replaceable persistence provider.
/// </summary>
public interface ISnippetRepository
{
    /// <summary>
    /// Loads and validates all categories and questions without applying access or language policy.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete immutable domain library.</returns>
    Task<SnippetLibrary> LoadAsync(CancellationToken cancellationToken);
}

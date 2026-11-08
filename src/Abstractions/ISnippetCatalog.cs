using Models;
using Models.Snippets;

namespace Abstractions;

/// <summary>
/// Defines startup initialization and authorized access to an in-memory snippet library.
/// </summary>
public interface ISnippetCatalog
{
    /// <summary>
    /// Gets whether the library feature is enabled for this application lifetime.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when initialization has not completed.</exception>
    bool Enabled { get; }

    /// <summary>
    /// Loads the library before requests are served and rejects calls after successful initialization.
    /// </summary>
    /// <param name="cancellationToken">Cancels the semaphore wait or this caller's repository load.</param>
    /// <returns>Completion of initialization. Failed or canceled loads leave the catalog uninitialized.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the catalog has already been initialized successfully.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the semaphore wait or repository load is canceled.</exception>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether an authenticated administrator can use this library.
    /// </summary>
    /// <param name="adminUser">The current authenticated administrator.</param>
    /// <returns>True when the feature and role allow access.</returns>
    /// <exception cref="ArgumentException">Thrown when the administrator is uninitialized.</exception>
    /// <exception cref="InvalidOperationException">Thrown when initialization has not completed.</exception>
    bool CanAccess(AdminUser adminUser);

    /// <summary>
    /// Returns the loaded library for an authorized administrator.
    /// </summary>
    /// <param name="adminUser">The current authenticated administrator.</param>
    /// <returns>The immutable library filtered to configured languages.</returns>
    /// <exception cref="ArgumentException">Thrown when the administrator is uninitialized.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when access is not allowed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when startup initialization has not completed.</exception>
    SnippetLibrary GetSnapshot(AdminUser adminUser);
}

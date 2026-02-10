using Models;

namespace SealCode.Security;

/// <summary>
/// Defines access to the current admin user.
/// </summary>
internal interface IAdminUserManager
{
    /// <summary>
    /// Determines whether the current request is authenticated as admin.
    /// </summary>
    /// <returns>True when authenticated as admin; otherwise false.</returns>
    bool IsAdmin();

    /// <summary>
    /// Tries to get the authenticated admin user from the current request.
    /// </summary>
    /// <param name="adminUser">The authenticated admin user when found.</param>
    /// <returns>True when the admin is authenticated; otherwise false.</returns>
    bool TryGetAdminUser(out AdminUser adminUser);

    /// <summary>
    /// Tries to authenticate and set the current admin user based on login form data.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when authentication succeeds; otherwise false.</returns>
    Task<bool> TrySetCurrentUserAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Clears the current admin user authentication cookie.
    /// </summary>
    void ClearCurrentAdminUser();
}

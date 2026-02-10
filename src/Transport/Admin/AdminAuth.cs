using Microsoft.AspNetCore.Http;

using Models.Configuration;

namespace Transport.Admin;

/// <summary>
/// Provides admin authentication helpers.
/// </summary>
public static class AdminAuth
{
    /// <summary>
    /// Gets the admin cookie name.
    /// </summary>
    public const string COOKIENAME = "admin_auth";

    /// <summary>
    /// Determines whether the request is authenticated as admin.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="settings">The application configuration.</param>
    /// <returns>True when admin cookie is present and matches a configured admin user; otherwise false.</returns>
    public static bool IsAdmin(HttpContext context, ApplicationConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        return TryGetAdminUser(context, settings, out _);
    }

    /// <summary>
    /// Tries to get the authenticated admin name from the request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="settings">The application configuration.</param>
    /// <param name="adminName">The authenticated admin name when found.</param>
    /// <returns>True when the cookie is present and matches a configured admin user; otherwise false.</returns>
    public static bool TryGetAdminName(HttpContext context, ApplicationConfiguration settings, out string adminName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        adminName = string.Empty;
        if (!TryGetAdminUser(context, settings, out var user))
        {
            return false;
        }

        adminName = user.Name;
        return true;
    }

    /// <summary>
    /// Tries to get the authenticated admin user from the request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="settings">The application configuration.</param>
    /// <param name="adminUser">The authenticated admin user when found.</param>
    /// <returns>True when the cookie is present and matches a configured admin user; otherwise false.</returns>
    public static bool TryGetAdminUser(HttpContext context, ApplicationConfiguration settings, out AdminUserConfiguration adminUser)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);

        adminUser = null!;
        if (!context.Request.Cookies.TryGetValue(COOKIENAME, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var user = settings.AdminUsers.FirstOrDefault(user =>
            string.Equals(user.Name, value, StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            return false;
        }

        adminUser = user;
        return true;
    }
}

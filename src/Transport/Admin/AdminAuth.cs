using Microsoft.AspNetCore.Http;

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
    /// <returns>True when admin cookie is present; otherwise false.</returns>
    public static bool IsAdmin(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Request.Cookies.TryGetValue(COOKIENAME, out var value) && value == "1";
    }
}

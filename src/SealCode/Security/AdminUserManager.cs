using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

namespace SealCode.Security;

/// <summary>
/// Default admin user manager.
/// </summary>
internal sealed class AdminUserManager : IAdminUserManager
{
    /// <summary>
    /// Gets the admin cookie name.
    /// </summary>
    private const string COOKIENAME = "admin_auth";

    /// <summary>
    /// Initializes a new instance of the <see cref="AdminUserManager"/> class.
    /// </summary>
    /// <param name="settings">The application configuration.</param>
    /// <param name="context">The HTTP context.</param>
    public AdminUserManager(IOptions<ApplicationConfiguration> settings, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        _settings = settings.Value;
        _context = context;
    }

    /// <inheritdoc />
    public bool IsAdmin() => TryGetAdminUser(out _);

    /// <inheritdoc />
    public bool TryGetAdminUser(out AdminUser adminUser)
    {
        adminUser = default;
        if (!_context.Request.Cookies.TryGetValue(COOKIENAME, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var user = _settings.AdminUsers.FirstOrDefault(user =>
            string.Equals(user.Name, value, StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            return false;
        }

        adminUser = new AdminUser(user.Name, user.IsSuperAdmin);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TrySetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var form = await _context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        var name = form["name"].ToString().Trim();
        var password = form["password"].ToString();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var user = _settings.AdminUsers.FirstOrDefault(user =>
            string.Equals(user.Name, name, StringComparison.OrdinalIgnoreCase)
            && user.Password == password);
        if (user is null)
        {
            return false;
        }

        _context.Response.Cookies.Append(COOKIENAME, user.Name, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax
        });

        return true;
    }

    /// <inheritdoc />
    public void ClearCurrentAdminUser()
        => _context.Response.Cookies.Delete(COOKIENAME);

    private readonly ApplicationConfiguration _settings;
    private readonly HttpContext _context;
}

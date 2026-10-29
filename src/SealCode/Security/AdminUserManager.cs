using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
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
    /// Initializes a new instance of the <see cref="AdminUserManager"/> class.
    /// </summary>
    /// <param name="settings">The application configuration.</param>
    /// <param name="accessor">The current HTTP context accessor.</param>
    public AdminUserManager(
        IOptions<ApplicationConfiguration> settings,
        IHttpContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(accessor);

        _settings = settings.Value;
        _accessor = accessor;
    }

    /// <inheritdoc />
    public bool IsAdmin() => TryGetAdminUser(out _);

    /// <inheritdoc />
    public bool TryGetAdminUser(out AdminUser adminUser)
    {
        var context = Context;
        adminUser = default;
        if (context.User.Identity?.IsAuthenticated != true
            || string.IsNullOrWhiteSpace(context.User.Identity.Name))
        {
            return false;
        }

        if (context.User.Identity.AuthenticationType == CookieAuthenticationDefaults.AuthenticationScheme
            && context.User.FindFirstValue("admin_provider") == "google")
        {
            var subject = context.User.FindFirstValue("google_sub");
            var identity = new GoogleIdentity(
                subject ?? "",
                context.User.FindFirstValue(ClaimTypes.Email) ?? "",
                context.User.FindFirstValue("google_hd") ?? "",
                true);
            if (context.User.Identity.Name == $"google:{subject}"
                && GoogleSignInAccess.IsAllowed(identity, _settings.GoogleSignIn))
            {
                adminUser = new AdminUser(context.User.Identity.Name, displayName: identity.Email);
                return true;
            }
            return false;
        }

        var user = _settings.AdminUsers.FirstOrDefault(user =>
            string.Equals(user.Name.Trim(), context.User.Identity.Name, StringComparison.OrdinalIgnoreCase));
        if (user is null || context.User.FindFirstValue("credential") != CredentialStamp(user))
        {
            return false;
        }

        adminUser = new AdminUser(user.Name, user.IsSuperAdmin);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> TrySetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var context = Context;
        var form = await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        var name = form["name"].ToString().Trim();
        var password = form["password"].ToString();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var user = _settings.AdminUsers.FirstOrDefault(user =>
            string.Equals(user.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        var hasher = new PasswordHasher<AdminUserConfiguration>();
        var hash = user?.PasswordHash;
        // Legacy settings are accepted only by Development startup validation.
        if (user is not null && string.IsNullOrEmpty(hash) && user.Password is not null)
        {
            hash = hasher.HashPassword(user, user.Password);
        }

        var verificationUser = user ?? new AdminUserConfiguration { Name = "unknown" };
        if (string.IsNullOrEmpty(hash))
        {
            hash = _dummyHash;
        }

        if (hasher.VerifyHashedPassword(verificationUser, hash, password) == PasswordVerificationResult.Failed || user is null)
        {
            return false;
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, user.Name.Trim()), new Claim("credential", CredentialStamp(user))],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity)).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public Task ClearCurrentAdminUserAsync()
        => Context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <inheritdoc />
    public async Task<bool> TrySetGoogleCurrentUserAsync(GoogleIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(_settings.GoogleSignIn.ClientId)
            || !GoogleSignInAccess.IsAllowed(identity, _settings.GoogleSignIn))
        {
            return false;
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, $"google:{identity.Subject}"),
            new Claim(ClaimTypes.Email, identity.Email),
            new Claim("admin_provider", "google"),
            new Claim("google_sub", identity.Subject),
            new Claim("google_hd", identity.HostedDomain)
        };
        await Context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)))
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Creates the credential fingerprint bound to an admin authentication ticket.
    /// </summary>
    /// <param name="user">The configured admin account.</param>
    /// <returns>The hexadecimal SHA-256 fingerprint of the configured credential.</returns>
    internal static string CredentialStamp(AdminUserConfiguration user)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash ?? user.Password ?? "")));

    private static readonly string _dummyHash = new PasswordHasher<AdminUserConfiguration>().HashPassword(new() { Name = "unknown" }, Guid.NewGuid().ToString());
    private readonly ApplicationConfiguration _settings;
    private HttpContext Context => _accessor.HttpContext
        ?? throw new InvalidOperationException("HttpContext is not available outside an HTTP request.");

    private readonly IHttpContextAccessor _accessor;
}

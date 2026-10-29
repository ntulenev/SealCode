using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

using Models.Configuration;

namespace SealCode.Security;

/// <summary>
/// Authenticates MCP requests independently of browser cookies.
/// </summary>
internal sealed class McpTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    internal const string SCHEME_NAME = "AdminMcpToken";

    /// <summary>
    /// Initializes bearer authentication for the configured admin accounts.
    /// </summary>
    /// <param name="options">The authentication scheme options.</param>
    /// <param name="logger">The authentication logger factory.</param>
    /// <param name="encoder">The URL encoder.</param>
    /// <param name="settings">The validated application settings.</param>
    public McpTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<ApplicationConfiguration> settings)
        : base(options, logger, encoder)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings.Value;
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (Request.Headers.Authorization.Count != 1
            || !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter)
            || header.Parameter.Length is < 32 or > 1024)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid MCP bearer token."));
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(header.Parameter));
        AdminUserConfiguration? matched = null;
        foreach (var admin in _settings.AdminUsers)
        {
            if (!string.IsNullOrEmpty(admin.McpToken)
                && CryptographicOperations.FixedTimeEquals(suppliedHash,
                    SHA256.HashData(Encoding.UTF8.GetBytes(admin.McpToken))))
            {
                matched = admin;
            }
        }

        if (matched is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid MCP bearer token."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, matched.Name.Trim()),
             new Claim(ClaimTypes.Name, matched.Name.Trim()),
             new Claim("credential", AdminUserManager.CredentialStamp(matched))], SCHEME_NAME);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SCHEME_NAME)));
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    private readonly ApplicationConfiguration _settings;
}

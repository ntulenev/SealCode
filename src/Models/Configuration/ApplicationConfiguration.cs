using System.ComponentModel.DataAnnotations;

namespace Models.Configuration;

/// <summary>
/// Application configuration settings.
/// </summary>
public sealed class ApplicationConfiguration
{
    /// <summary>Optional Google Workspace sign-in settings.</summary>
    public GoogleSignInConfiguration GoogleSignIn { get; init; } = new();

    /// <summary>
    /// Gets trusted reverse proxy IP addresses.
    /// </summary>
    public string[] TrustedProxies { get; init; } = [];

    /// <summary>
    /// Gets resource budgets.
    /// </summary>
    public RoomLimits Limits { get; init; } = new();

    /// <summary>
    /// Gets or sets the admin users.
    /// </summary>
    [Required]
    [MinLength(1)]
    public required AdminUserConfiguration[] AdminUsers { get; init; }

    /// <summary>
    /// Gets or sets the allowed room languages.
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string[] Languages { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of users per room.
    /// </summary>
    [Range(1, 5)]
    public int MaxUsersPerRoom { get; init; }

    /// <summary>
    /// Gets or sets whether selective collaborative undo and redo are enabled.
    /// </summary>
    public bool CollaborativeUndoEnabled { get; init; } = true;
}

/// <summary>Controls which Google-hosted organizations may sign in as ordinary admins.</summary>
public sealed class GoogleSignInConfiguration
{
    /// <summary>Web application OAuth client ID. Omit to disable Google sign-in.</summary>
    public string? ClientId { get; init; }

    /// <summary>Exact hosted domains accepted from verified Google ID tokens.</summary>
    public string[] AllowedHostedDomains { get; init; } = [];
}

/// <summary>
/// Represents an admin user from configuration.
/// </summary>
public sealed class AdminUserConfiguration : IValidatableObject
{
    /// <summary>
    /// Gets or sets the admin display name.
    /// </summary>
    [Required]
    [MinLength(2)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets or sets the admin password.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Gets the ASP.NET Core Identity password hash.
    /// </summary>
    public string? PasswordHash { get; init; }

    /// <summary>
    /// Gets the optional bearer token for MCP access as this admin.
    /// </summary>
    public string? McpToken { get; init; }

    /// <summary>
    /// Gets or sets whether the admin can manage rooms created by other admins.
    /// </summary>
    public bool IsSuperAdmin { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(PasswordHash) && (string.IsNullOrWhiteSpace(Password) || Password.Length < 4))
        {
            yield return new ValidationResult("A password hash is required.");
        }
    }
}

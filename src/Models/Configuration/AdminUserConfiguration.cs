using System.ComponentModel.DataAnnotations;

namespace Models.Configuration;

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

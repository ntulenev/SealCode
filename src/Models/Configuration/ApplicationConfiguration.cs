using System.ComponentModel.DataAnnotations;

namespace Models.Configuration;

/// <summary>
/// Application configuration settings.
/// </summary>
public sealed class ApplicationConfiguration
{
    /// <summary>
    /// Gets or sets the admin password.
    /// </summary>
    [Required]
    [MinLength(4)]
    public required string AdminPassword { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of users per room.
    /// </summary>
    [Range(1, 5)]
    public int MaxUsersPerRoom { get; init; }
}

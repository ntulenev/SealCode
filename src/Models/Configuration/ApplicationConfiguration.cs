using System.ComponentModel.DataAnnotations;

namespace Models.Configuration;

/// <summary>
/// Application configuration settings.
/// </summary>
public sealed class ApplicationConfiguration
{
    /// <summary>Gets the independent presentation-viewer budgets.</summary>
    public ObserverLimits Observers { get; init; } = new();
    /// <summary>
    /// Gets startup availability and access settings for the private snippet library.
    /// </summary>
    public SnippetConfiguration Snippets { get; init; } = new();

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

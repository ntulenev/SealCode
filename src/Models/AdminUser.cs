namespace Models;

/// <summary>
/// Represents an admin user.
/// </summary>
public readonly record struct AdminUser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdminUser"/> struct.
    /// </summary>
    /// <param name="name">The stable admin identity.</param>
    /// <param name="isSuperAdmin">True when the admin can manage other admins' rooms.</param>
    /// <param name="displayName">Optional name shown to other administrators.</param>
    /// <exception cref="ArgumentException">Thrown when the value is empty.</exception>
    public AdminUser(string name, bool isSuperAdmin = false, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Admin name is required.", nameof(name));
        }

        Name = name.Trim();
        IsSuperAdmin = isSuperAdmin;
        DisplayName = displayName ?? Name;
    }

    /// <summary>
    /// Gets the stable admin identity used for room ownership.
    /// </summary>
    public string Name { get; }

    /// <summary>Human-readable name shown for room ownership.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets a value indicating whether the admin can manage rooms created by others.
    /// </summary>
    public bool IsSuperAdmin { get; }

    /// <summary>
    /// Determines whether the provided admin represents the same user.
    /// </summary>
    /// <param name="other">The other admin user.</param>
    /// <returns>True for the same identity; Google subjects are case-sensitive.</returns>
    public bool Matches(AdminUser other)
        => string.Equals(Name, other.Name,
            Name.StartsWith("google:", StringComparison.Ordinal)
                || other.Name.StartsWith("google:", StringComparison.Ordinal)
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase);
}

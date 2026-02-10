namespace Models;

/// <summary>
/// Represents an admin user.
/// </summary>
public readonly record struct AdminUser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdminUser"/> struct.
    /// </summary>
    /// <param name="name">The admin display name.</param>
    /// <param name="isSuperAdmin">True when the admin can manage other admins' rooms.</param>
    /// <exception cref="ArgumentException">Thrown when the value is empty.</exception>
    public AdminUser(string name, bool isSuperAdmin = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Admin name is required.", nameof(name));
        }

        Name = name.Trim();
        IsSuperAdmin = isSuperAdmin;
    }

    /// <summary>
    /// Gets the admin display name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the admin can manage rooms created by others.
    /// </summary>
    public bool IsSuperAdmin { get; }

    /// <summary>
    /// Determines whether the provided admin represents the same user.
    /// </summary>
    /// <param name="other">The other admin user.</param>
    /// <returns>True when both users share the same name (case-insensitive).</returns>
    public bool Matches(AdminUser other)
        => string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
}

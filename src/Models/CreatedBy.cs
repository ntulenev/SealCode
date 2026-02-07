namespace Models;

/// <summary>
/// Represents the admin that created a room.
/// </summary>
public readonly record struct CreatedBy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreatedBy"/> struct.
    /// </summary>
    /// <param name="value">The admin name.</param>
    /// <exception cref="ArgumentException">Thrown when the value is empty.</exception>
    public CreatedBy(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Creator name is required.", nameof(value));
        }

        Value = value.Trim();
    }

    /// <summary>
    /// Gets the admin name value.
    /// </summary>
    public string Value { get; }
}

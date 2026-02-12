namespace Models;

/// <summary>
/// Represents a room user name.
/// </summary>
public readonly record struct RoomUser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomUser"/> struct.
    /// </summary>
    /// <param name="value">The room user name value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public RoomUser(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Display name is required", nameof(value));
        }

        Value = trimmed;
    }

    /// <summary>
    /// Gets the room user name value.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}


namespace Models;

/// <summary>
/// Represents a room name.
/// </summary>
public readonly record struct RoomName
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomName"/> struct.
    /// </summary>
    /// <param name="value">The name value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public RoomName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Room name is required", nameof(value));
        }

        if (trimmed.Length > 20)
        {
            trimmed = $"{trimmed[..20]}...";
        }

        Value = trimmed;
    }

    /// <summary>
    /// Gets the name value.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

namespace Models;

/// <summary>
/// Represents a room name.
/// </summary>
public readonly record struct RoomName(string Value)
{
    /// <summary>
    /// Creates a room name.
    /// </summary>
    /// <param name="value">The name value.</param>
    /// <returns>The room name.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public static RoomName Create(string value)
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

        return new RoomName(trimmed);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

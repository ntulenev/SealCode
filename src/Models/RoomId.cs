namespace Models;

/// <summary>
/// Represents a room identifier backed by a URL-safe, Base64-encoded GUID.
/// </summary>
public readonly record struct RoomId
{
    /// <summary>
    /// Creates a new room identifier.
    /// </summary>
    /// <returns>A new room identifier.</returns>
    public static RoomId New() => new(Guid.NewGuid());

    /// <summary>
    /// Initializes a new instance of the <see cref="RoomId"/> struct.
    /// </summary>
    /// <param name="value">The identifier value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    public RoomId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Room id is required", nameof(value));
        }

        if (!ShortGuid.TryParse(trimmed, out var shortGuid) || shortGuid.Value == Guid.Empty)
        {
            throw new ArgumentException("Room id must be a valid GUID.", nameof(value));
        }

        Value = shortGuid.ToString();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoomId"/> struct.
    /// </summary>
    /// <param name="value">The GUID value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is <see cref="Guid.Empty"/>.</exception>
    public RoomId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Room id cannot be empty.", nameof(value));
        }

        Value = new ShortGuid(value).ToString();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoomId"/> struct.
    /// </summary>
    /// <param name="value">The short GUID value.</param>
    public RoomId(ShortGuid value)
    {
        if (value.Value == Guid.Empty)
        {
            throw new ArgumentException("Room id cannot be empty.", nameof(value));
        }

        Value = value.ToString();
    }

    /// <summary>
    /// Gets the identifier value (URL-safe short GUID).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Tries to parse a room identifier.
    /// </summary>
    /// <param name="value">The identifier value.</param>
    /// <param name="roomId">The parsed identifier.</param>
    /// <returns>True when parsed; otherwise false.</returns>
    public static bool TryParse(string? value, out RoomId roomId)
    {
        var trimmed = value?.Trim();
        if (!ShortGuid.TryParse(trimmed, out var shortGuid) || shortGuid.Value == Guid.Empty)
        {
            roomId = default;
            return false;
        }

        roomId = new RoomId(shortGuid);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

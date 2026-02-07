using System.Globalization;

namespace Models;

/// <summary>
/// Represents the room text content.
/// </summary>
public readonly record struct RoomText(string Value)
{
    /// <summary>
    /// Creates a <see cref="RoomText"/> from a string.
    /// </summary>
    /// <param name="value">The text value.</param>
    /// <returns>The room text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    public static RoomText From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new RoomText(value);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>
/// Represents the room version.
/// </summary>
public readonly record struct RoomVersion(int Value)
{
    /// <summary>
    /// Creates a <see cref="RoomVersion"/> from an integer.
    /// </summary>
    /// <param name="value">The version value.</param>
    /// <returns>The room version.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is less than 1.</exception>
    public static RoomVersion From(int value)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Version must be positive.");
        }

        return new RoomVersion(value);
    }

    /// <summary>
    /// Returns the next version value.
    /// </summary>
    /// <returns>The next room version.</returns>
    public RoomVersion Next() => new(Value + 1);

    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

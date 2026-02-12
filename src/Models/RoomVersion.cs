using System.Globalization;

namespace Models;

/// <summary>
/// Represents the room version.
/// </summary>
public readonly record struct RoomVersion
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomVersion"/> struct.
    /// </summary>
    /// <param name="value">The version value.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is less than 1.</exception>
    public RoomVersion(int value)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Version must be positive.");
        }

        Value = value;
    }

    /// <summary>
    /// Gets the version value.
    /// </summary>
    public int Value { get; }

    /// <summary>
    /// Returns the next version value.
    /// </summary>
    /// <returns>The next room version.</returns>
    public RoomVersion Next() => Value == int.MaxValue ? new(1) : new(Value + 1);

    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

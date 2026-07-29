using System.Globalization;

namespace Models;

/// <summary>
/// Represents the revision of the CRDT document stored in a room.
/// </summary>
public readonly record struct RoomDocumentRevision
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomDocumentRevision"/> struct.
    /// </summary>
    /// <param name="value">The revision value.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the revision is negative.</exception>
    public RoomDocumentRevision(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Document revision cannot be negative.");
        }

        Value = value;
    }

    /// <summary>
    /// Gets the revision value.
    /// </summary>
    public long Value { get; }

    /// <summary>
    /// Returns the next document revision.
    /// </summary>
    /// <returns>The next revision.</returns>
    /// <exception cref="OverflowException">Thrown when the revision cannot be incremented.</exception>
    public RoomDocumentRevision Next() => new(checked(Value + 1));

    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

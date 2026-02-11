namespace Models.Exceptions;

/// <summary>
/// Represents errors that occur when a room cannot be found.
/// </summary>
public sealed class RoomNotFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public RoomNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoomNotFoundException"/> class.
    /// </summary>
    public RoomNotFoundException()
        : base("Room not found")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoomNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner Exception.</param>
    public RoomNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

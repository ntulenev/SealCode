using System;

namespace Models;

/// <summary>
/// Represents errors that occur when adding a user to a room.
/// </summary>
public sealed class AddRoomUserException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddRoomUserException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AddRoomUserException(string message)
        : base(message)
    {
    }
}

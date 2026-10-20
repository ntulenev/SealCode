using Models;
using Models.Exceptions;

namespace Abstractions;

/// <summary>
/// Defines application operations for active room participants.
/// </summary>
public interface IRoomSessionService
{
    /// <summary>
    /// Gets the room and user associated with an active participant.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="user">When this method returns, contains the participant name.</param>
    /// <returns>The room containing the participant.</returns>
    /// <exception cref="RoomNotFoundException">Thrown when the room does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the connection is not a participant.</exception>
    RoomState GetParticipant(RoomId roomId, ConnectionId connection, out RoomUser user);

    /// <summary>
    /// Appends an update while membership and closure are checked atomically.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="update">The serialized incremental CRDT update.</param>
    /// <returns>The assigned document revision and room version.</returns>
    /// <exception cref="RoomNotFoundException">Thrown when the room does not exist or is closed.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the connection is not a participant.</exception>
    /// <exception cref="ArgumentException">Thrown when the update is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the update is null.</exception>
    /// <exception cref="RoomLimitException">Thrown when a resource budget is exceeded.</exception>
    RoomDocumentAppendResult Append(
        RoomId roomId,
        ConnectionId connection,
        byte[] update);

    /// <summary>
    /// Stores a checkpoint if it represents the current document revision.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="revision">The document revision represented by the checkpoint.</param>
    /// <param name="state">The serialized checkpoint.</param>
    /// <param name="text">The corresponding plain text snapshot.</param>
    /// <returns>True when the checkpoint was accepted; otherwise false.</returns>
    /// <exception cref="RoomNotFoundException">Thrown when the room does not exist or is closed.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the connection is not a participant.</exception>
    /// <exception cref="ArgumentException">Thrown when the checkpoint is empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the checkpoint is null.</exception>
    /// <exception cref="RoomLimitException">Thrown when a resource budget is exceeded.</exception>
    bool Checkpoint(
        RoomId roomId,
        ConnectionId connection,
        RoomDocumentRevision revision,
        byte[] state,
        RoomText text);

    /// <summary>
    /// Changes the language and captures its assigned metadata revision atomically.
    /// </summary>
    /// <param name="room">The room to update.</param>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="language">The new room language.</param>
    /// <returns>The room version and language metadata revision.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the room is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the language is not allowed.</exception>
    /// <exception cref="RoomNotFoundException">Thrown when the room is closed.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the connection is not a participant.</exception>
    RoomLanguageChangeResult ChangeLanguage(
        RoomState room,
        ConnectionId connection,
        RoomLanguage language);
}

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
    /// Looks up a participant without rejecting non-members.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="connection">The connection identifier.</param>
    /// <param name="room">The existing room.</param>
    /// <param name="user">The participant, if found.</param>
    /// <returns>Whether the connection belongs to the room.</returns>
    /// <exception cref="RoomNotFoundException">Thrown when the room does not exist.</exception>
    bool TryGetParticipant(RoomId roomId, ConnectionId connection, out RoomState room, out RoomUser user);

    /// <summary>
    /// Validates, decodes and appends a Base64 document update.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="updateBase64">The encoded update.</param>
    /// <returns>The assigned document revision and room version.</returns>
    /// <exception cref="RoomNotFoundException">The room is missing or closed.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller is not a participant.</exception>
    /// <exception cref="ArgumentException">The payload is invalid.</exception>
    /// <exception cref="RoomLimitException">A resource budget was exceeded.</exception>
    RoomSessionAppendResult Append(RoomId roomId, ConnectionId connection, string updateBase64);

    /// <summary>
    /// Validates and stores a checkpoint of the latest document revision.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="stateBase64">The encoded document state.</param>
    /// <param name="textSnapshot">The corresponding plain text.</param>
    /// <param name="basedOnRevision">The revision contained in the checkpoint.</param>
    /// <returns>Whether the checkpoint was accepted or stale.</returns>
    /// <exception cref="RoomNotFoundException">The room is missing or closed.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller is not a participant.</exception>
    /// <exception cref="ArgumentException">The payload or revision is invalid.</exception>
    /// <exception cref="RoomLimitException">A resource budget was exceeded.</exception>
    CheckpointOutcome Checkpoint(RoomId roomId, ConnectionId connection, string stateBase64, string? textSnapshot, long basedOnRevision);

    /// <summary>
    /// Changes the language and captures its assigned metadata revision atomically.
    /// </summary>
    /// <param name="roomId">The room identifier.</param>
    /// <param name="connection">The participant connection identifier.</param>
    /// <param name="language">The new room language.</param>
    /// <returns>The room version and language metadata revision.</returns>
    /// <exception cref="ArgumentException">Thrown when the language is not allowed.</exception>
    /// <exception cref="RoomNotFoundException">Thrown when the room is closed.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the connection is not a participant.</exception>
    RoomLanguageChangeResult ChangeLanguage(
        RoomId roomId,
        ConnectionId connection,
        RoomLanguage language);
}

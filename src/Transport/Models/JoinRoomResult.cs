using Models;

namespace Transport.Models;

/// <summary>
/// Represents the result of joining a room.
/// </summary>
/// <param name="Name">The room name.</param>
/// <param name="Language">The room language.</param>
/// <param name="Text">The room text.</param>
/// <param name="Version">The current room version.</param>
/// <param name="Users">The display names of users currently in the room.</param>
/// <param name="CreatedBy">The display name of the room creator.</param>
/// <param name="DocumentRevision">The latest CRDT document revision.</param>
/// <param name="CheckpointRevision">The revision represented by <paramref name="YjsState"/>.</param>
/// <param name="YjsState">Optional checkpoint state for the CRDT document.</param>
/// <param name="YjsUpdates">The incremental updates after the checkpoint.</param>
public sealed record JoinRoomResult(
    string Name,
    string Language,
    string Text,
    int Version,
    string[] Users,
    string CreatedBy,
    long DocumentRevision,
    long CheckpointRevision,
    string? YjsState,
    YjsDocumentUpdate[] YjsUpdates)
{
    /// <summary>
    /// Gets the independent language revision.
    /// </summary>
    public long MetadataRevision { get; init; }

    /// <summary>
    /// Gets the independent membership revision.
    /// </summary>
    public long PresenceRevision { get; init; }

    /// <summary>
    /// Creates a join-room result from room state.
    /// </summary>
    /// <param name="room">The source room state.</param>
    /// <returns>The mapped join-room result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="room"/> is null.</exception>
    public static JoinRoomResult From(RoomState room)
    {
        ArgumentNullException.ThrowIfNull(room);
        var syncState = room.CreateYjsSyncState();

        return new JoinRoomResult(
            room.Name.Value,
            syncState.Language.Value,
            syncState.Text.Value,
            syncState.Version.Value,
            [.. syncState.Users.Select(x => x.Value)],
            room.CreatedBy.Name,
            syncState.DocumentRevision.Value,
            syncState.CheckpointRevision.Value,
            syncState.Checkpoint.Length > 0 ? Convert.ToBase64String(syncState.Checkpoint) : null,
            [.. syncState.Updates.Select(update => new YjsDocumentUpdate(
                update.Revision.Value,
                Convert.ToBase64String(update.Payload)))])
        {
            MetadataRevision = syncState.MetadataRevision,
            PresenceRevision = syncState.PresenceRevision
        };
    }
}

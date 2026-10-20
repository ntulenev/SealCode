namespace Models;

/// <summary>
/// Represents an atomic snapshot used to synchronize a CRDT document.
/// </summary>
/// <param name="DocumentRevision">The latest document revision.</param>
/// <param name="CheckpointRevision">The revision represented by the checkpoint.</param>
/// <param name="Checkpoint">The encoded checkpoint state.</param>
/// <param name="Updates">The updates after the checkpoint.</param>
public sealed record RoomDocumentSyncState(
    RoomDocumentRevision DocumentRevision,
    RoomDocumentRevision CheckpointRevision,
    byte[] Checkpoint,
    RoomDocumentUpdate[] Updates)
{
    /// <summary>Gets the language from the same atomic snapshot.</summary>
    public RoomLanguage Language { get; init; }
    /// <summary>Gets the text from the same atomic snapshot.</summary>
    public RoomText Text { get; init; }
    /// <summary>Gets the room version.</summary>
    public RoomVersion Version { get; init; }
    /// <summary>Gets the language revision.</summary>
    public long MetadataRevision { get; init; }
    /// <summary>Gets the membership revision.</summary>
    public long PresenceRevision { get; init; }
    /// <summary>Gets a detached membership snapshot.</summary>
    public RoomUser[] Users { get; init; } = [];
}

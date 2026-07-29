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
    RoomDocumentUpdate[] Updates);

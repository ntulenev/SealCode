namespace Models;

/// <summary>
/// Describes the revisions assigned after appending a CRDT update.
/// </summary>
/// <param name="DocumentRevision">The assigned document revision.</param>
/// <param name="RoomVersion">The resulting room version.</param>
public readonly record struct RoomDocumentAppendResult(
    RoomDocumentRevision DocumentRevision,
    RoomVersion RoomVersion);

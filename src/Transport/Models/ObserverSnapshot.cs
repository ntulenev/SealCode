namespace Transport.Models;

/// <summary>Immutable ObserverSnapshot wire payload.</summary>
/// <param name="Type">The Type wire value.</param>
/// <param name="Name">The Name wire value.</param>
/// <param name="Kind">The Kind wire value.</param>
/// <param name="Language">The Language wire value.</param>
/// <param name="Text">The Text wire value.</param>
/// <param name="Version">The Version wire value.</param>
/// <param name="MetadataRevision">The MetadataRevision wire value.</param>
/// <param name="PresenceRevision">The PresenceRevision wire value.</param>
/// <param name="Users">The Users wire value.</param>
/// <param name="DocumentRevision">The DocumentRevision wire value.</param>
/// <param name="CheckpointRevision">The CheckpointRevision wire value.</param>
/// <param name="YjsState">The YjsState wire value.</param>
/// <param name="YjsUpdates">The YjsUpdates wire value.</param>
public sealed record ObserverSnapshot(
    string Type,
    string Name,
    string Kind,
    string Language,
    string Text,
    int Version,
    long MetadataRevision,
    long PresenceRevision,
    string[] Users,
    long DocumentRevision,
    long CheckpointRevision,
    string? YjsState,
    YjsDocumentUpdate[] YjsUpdates);

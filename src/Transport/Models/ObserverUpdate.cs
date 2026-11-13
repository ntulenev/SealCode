namespace Transport.Models;

/// <summary>Immutable ObserverUpdate wire payload.</summary>
/// <param name="Type">The Type wire value.</param>
/// <param name="DocumentRevision">The DocumentRevision wire value.</param>
/// <param name="CheckpointRevision">The CheckpointRevision wire value.</param>
/// <param name="RequiresResync">The RequiresResync wire value.</param>
/// <param name="YjsUpdates">The YjsUpdates wire value.</param>
/// <param name="Language">The Language wire value.</param>
/// <param name="MetadataRevision">The MetadataRevision wire value.</param>
/// <param name="PresenceRevision">The PresenceRevision wire value.</param>
/// <param name="Users">The Users wire value.</param>
/// <param name="ObserverCount">The ObserverCount wire value.</param>
/// <param name="ObserverRevision">The ObserverRevision wire value.</param>
public sealed record ObserverUpdate(
    string Type,
    long DocumentRevision,
    long CheckpointRevision,
    bool RequiresResync,
    YjsDocumentUpdate[] YjsUpdates,
    string Language,
    long MetadataRevision,
    long PresenceRevision,
    string[] Users,
    int ObserverCount,
    long ObserverRevision);

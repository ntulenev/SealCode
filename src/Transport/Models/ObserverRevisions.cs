using Models;

namespace Transport.Models;

/// <summary>Immutable ObserverRevisions wire payload.</summary>
/// <param name="Revisions">The Revisions wire value.</param>
/// <param name="ObserverCount">The ObserverCount wire value.</param>
/// <param name="ObserverRevision">The ObserverRevision wire value.</param>
public sealed record ObserverRevisions(
    RoomRevisions Revisions,
    int ObserverCount,
    long ObserverRevision);

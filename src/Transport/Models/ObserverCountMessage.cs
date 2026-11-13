namespace Transport.Models;

/// <summary>Immutable ObserverCount wire payload.</summary>
/// <param name="Type">The Type wire value.</param>
/// <param name="ObserverCount">The ObserverCount wire value.</param>
/// <param name="ObserverRevision">The ObserverRevision wire value.</param>
public sealed record ObserverCountMessage(
    string Type,
    int ObserverCount,
    long ObserverRevision);

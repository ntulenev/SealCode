namespace Transport.Models;

/// <summary>Immutable ObserverRequiresResync wire payload.</summary>
/// <param name="Type">The Type wire value.</param>
public sealed record ObserverRequiresResync(
    string Type);

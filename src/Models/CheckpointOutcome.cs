namespace Models;

/// <summary>
/// Describes whether a document checkpoint was accepted or superseded.
/// </summary>
public enum CheckpointOutcome
{
    /// <summary>
    /// The checkpoint represents the current document.
    /// </summary>
    Accepted,
    /// <summary>
    /// A newer document revision exists.
    /// </summary>
    Stale
}

namespace Models.Exceptions;

/// <summary>
/// A permanent resource refusal; the caller must retain its unsent changes.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032", Justification = "Stable protocol refusal has no arbitrary message.")]
public sealed class RoomLimitException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomLimitException"/> class.
    /// </summary>
    public RoomLimitException()
        : this("Room resource budget reached. Copy unsynced text before reloading.")
    {
    }

    /// <summary>
    /// Initializes a refusal for a specific resource budget.
    /// </summary>
    /// <param name="resource">The stable resource label.</param>
    public RoomLimitException(string resource)
        : base($"LimitExceeded: {resource}")
    {
        Resource = resource;
    }

    /// <summary>
    /// Gets the stable resource label used in the protocol refusal.
    /// </summary>
    public string Resource { get; }
}

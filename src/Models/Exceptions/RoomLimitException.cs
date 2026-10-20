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
        : base("LimitExceeded: Room resource budget reached. Copy unsynced text before reloading.")
    {
    }
}

namespace Models.Exceptions;

/// <summary>A permanent resource refusal; the caller must retain its unsent changes.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032", Justification = "Stable protocol refusal has no arbitrary message.")]
public sealed class RoomLimitException : Exception
{
    /// <summary>Creates a stable limit refusal.</summary>
    public RoomLimitException() : base("LimitExceeded: Room resource budget reached. Copy unsynced text before reloading.") { }
}

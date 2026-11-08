namespace SealCode.Hub;

/// <summary>
/// Stable categories of hub failures.
/// </summary>
internal enum HubErrorCode
{
    /// <summary>
    /// The caller supplied an invalid room identifier or payload.
    /// </summary>
    InvalidInput,

    /// <summary>
    /// A configured room or message resource budget was exceeded.
    /// </summary>
    LimitExceeded,

    /// <summary>
    /// A connection or invocation rate limit was reached.
    /// </summary>
    RateLimited,

    /// <summary>
    /// The requested room no longer exists.
    /// </summary>
    RoomNotFound,

    /// <summary>
    /// The connection must join the room before changing it.
    /// </summary>
    NotParticipant
}

using Microsoft.AspNetCore.SignalR;

using Models.Exceptions;

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

/// <summary>
/// Creates errors while retaining the existing wire messages.
/// </summary>
internal static class HubErrors
{
    /// <summary>
    /// Creates a categorized error. Legacy unprefixed input messages retain their format.
    /// </summary>
    /// <param name="code">The stable category of the hub failure.</param>
    /// <param name="message">The error details retained in the wire message.</param>
    /// <returns>A hub exception with the existing protocol error message.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the error code is not defined.</exception>
    public static HubException Create(HubErrorCode code, string message)
        => Create(code, message, legacyInput: false);

    /// <summary>
    /// Preserves legacy input errors that were sent without a category prefix.
    /// </summary>
    /// <param name="code">The stable category of the hub failure.</param>
    /// <param name="message">The error details retained in the wire message.</param>
    /// <param name="legacyInput">Whether an invalid-input error must retain its legacy unprefixed message.</param>
    /// <returns>A hub exception with the existing protocol error message.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the error code is not defined.</exception>
    public static HubException Create(HubErrorCode code, string message, bool legacyInput)
        => new(code switch
        {
            HubErrorCode.RoomNotFound => "Room not found",
            HubErrorCode.NotParticipant => "Join the room before changing it",
            HubErrorCode.InvalidInput when legacyInput => message,
            HubErrorCode.InvalidInput or HubErrorCode.LimitExceeded or HubErrorCode.RateLimited => $"{code}: {message}",
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown hub error code")
        });

    /// <summary>
    /// Translates application failures into the existing hub wire messages.
    /// </summary>
    /// <typeparam name="T">The application operation result type.</typeparam>
    /// <param name="invoke">The application operation whose failures are translated.</param>
    /// <returns>The application operation result when no translated failure occurs.</returns>
    /// <exception cref="HubException">Thrown when the application operation fails with a translated domain or input error.</exception>
    public static T Invoke<T>(Func<T> invoke)
    {
        try
        {
            return invoke();
        }
        catch (RoomNotFoundException)
        {
            throw Create(HubErrorCode.RoomNotFound, "");
        }
        catch (UnauthorizedAccessException)
        {
            throw Create(HubErrorCode.NotParticipant, "");
        }
        catch (RoomLimitException ex)
        {
            throw Create(HubErrorCode.LimitExceeded, ex.Message["LimitExceeded: ".Length..]);
        }
        catch (ArgumentException ex)
        {
            throw Create(HubErrorCode.InvalidInput, ex.Message, legacyInput: true);
        }
    }
}

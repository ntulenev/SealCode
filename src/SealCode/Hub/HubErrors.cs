using Microsoft.AspNetCore.SignalR;

using Models.Exceptions;

namespace SealCode.Hub;

/// <summary>
/// Stable categories of hub failures.
/// </summary>
internal enum HubErrorCode
{
    InvalidInput,
    LimitExceeded,
    RateLimited,
    RoomNotFound,
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
    public static HubException Create(HubErrorCode code, string message)
        => Create(code, message, legacyInput: false);

    /// <summary>
    /// Preserves legacy input errors that were sent without a category prefix.
    /// </summary>
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

using FluentAssertions;

using Microsoft.AspNetCore.SignalR;

using SealCode.Hub;

namespace SealCode.Tests;

internal static class HubErrorAssertions
{
    internal static void AssertHubError(HubException error, HubErrorCode expected)
    {
        var message = error.Message;
        HubErrorCode? actual = message.Contains("LimitExceeded", StringComparison.Ordinal)
            || message.Contains("too large", StringComparison.Ordinal) ? HubErrorCode.LimitExceeded
            : message.Contains("RateLimited", StringComparison.Ordinal) ? HubErrorCode.RateLimited
            : message.Contains("Room not found", StringComparison.Ordinal) ? HubErrorCode.RoomNotFound
            : message.Contains("Join the room before changing it", StringComparison.Ordinal) ? HubErrorCode.NotParticipant
            : message.StartsWith("InvalidInput:", StringComparison.Ordinal) ? HubErrorCode.InvalidInput
            : null;
        // Legacy input errors deliberately retain their unprefixed wire text.
        if (actual is null && (message.StartsWith("Invalid ", StringComparison.Ordinal)
            || message.StartsWith("Display name", StringComparison.Ordinal)
            || message.StartsWith("Room is full", StringComparison.Ordinal)
            || message.EndsWith("payload required", StringComparison.Ordinal)))
        {
            actual = HubErrorCode.InvalidInput;
        }
        actual.Should().Be(expected);
    }
}

using System.Text.RegularExpressions;

using FluentAssertions;

using Microsoft.AspNetCore.SignalR;

using SealCode.Hub;

namespace SealCode.Tests;

internal static partial class HubErrorAssertions
{
    internal static void AssertHubError(HubException error, HubErrorCode? expected)
    {
        var message = error.Message;
        HubErrorCode? actual = Limit().IsMatch(message) ? HubErrorCode.LimitExceeded
            : Rate().IsMatch(message) ? HubErrorCode.RateLimited
            : Invalid().IsMatch(message) ? HubErrorCode.InvalidInput
            : Missing().IsMatch(message) ? HubErrorCode.RoomNotFound
            : Participant().IsMatch(message) ? HubErrorCode.NotParticipant
            : null;
        actual.Should().Be(expected);
    }

    internal static void AssertLegacyHubError(HubException error, string exactMessage)
    {
        AssertHubError(error, null);
        error.Message.Should().Be(exactMessage);
    }

    [GeneratedRegex("LimitExceeded|too large", RegexOptions.ECMAScript | RegexOptions.CultureInvariant)]
    private static partial Regex Limit();

    [GeneratedRegex("RateLimited", RegexOptions.ECMAScript | RegexOptions.CultureInvariant)]
    private static partial Regex Rate();

    [GeneratedRegex("InvalidInput", RegexOptions.ECMAScript | RegexOptions.CultureInvariant)]
    private static partial Regex Invalid();

    [GeneratedRegex(@"\bRoom not found\b", RegexOptions.ECMAScript | RegexOptions.CultureInvariant)]
    private static partial Regex Missing();

    [GeneratedRegex("Join the room before changing it", RegexOptions.ECMAScript | RegexOptions.CultureInvariant)]
    private static partial Regex Participant();
}

namespace SealCode.Security;

/// <summary>Identity claims obtained from a Google-signed ID token.</summary>
/// <param name="Subject">The stable, case-sensitive Google account identifier.</param>
/// <param name="Email">The account's full email address.</param>
/// <param name="HostedDomain">The Workspace hosted domain, or null for accounts without one.</param>
/// <param name="EmailVerified">Whether Google has verified the email address.</param>
internal sealed record GoogleIdentity(string Subject, string Email, string? HostedDomain, bool EmailVerified);

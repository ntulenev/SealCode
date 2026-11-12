namespace SealCode.Security;

/// <summary>Identity claims obtained from a Google-signed ID token.</summary>
internal sealed record GoogleIdentity(string Subject, string Email, string? HostedDomain, bool EmailVerified);

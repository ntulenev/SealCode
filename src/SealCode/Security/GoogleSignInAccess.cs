using Models.Configuration;

namespace SealCode.Security;

/// <summary>Applies domain and individual-email access rules to verified Google claims.</summary>
internal static class GoogleSignInAccess
{
    /// <summary>Checks whether a verified identity matches either configured allowlist.</summary>
    /// <param name="identity">The verified Google claims, or null when verification failed.</param>
    /// <param name="config">The validated Google sign-in configuration.</param>
    /// <returns>True for a valid subject and verified email matching a domain or full email address.</returns>
    public static bool IsAllowed(GoogleIdentity? identity, GoogleSignInConfiguration config)
        => identity is { EmailVerified: true }
            && !string.IsNullOrWhiteSpace(identity.Subject)
            && identity.Subject.Length <= 255
            && !string.IsNullOrWhiteSpace(identity.Email)
            && (config.AllowedEmails.Contains(identity.Email, StringComparer.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(identity.HostedDomain)
                    && config.AllowedHostedDomains.Contains(identity.HostedDomain, StringComparer.OrdinalIgnoreCase)));
}

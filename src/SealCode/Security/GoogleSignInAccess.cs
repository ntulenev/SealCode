using Models.Configuration;

namespace SealCode.Security;

/// <summary>Applies the local organization's access policy to verified Google claims.</summary>
internal static class GoogleSignInAccess
{
    public static bool IsAllowed(GoogleIdentity? identity, GoogleSignInConfiguration config)
        => identity is { EmailVerified: true }
            && !string.IsNullOrWhiteSpace(identity.Subject)
            && identity.Subject.Length <= 255
            && !string.IsNullOrWhiteSpace(identity.Email)
            && (config.AllowedEmails.Contains(identity.Email, StringComparer.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(identity.HostedDomain)
                    && config.AllowedHostedDomains.Contains(identity.HostedDomain, StringComparer.OrdinalIgnoreCase)));
}

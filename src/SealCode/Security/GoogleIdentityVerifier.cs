using Google.Apis.Auth;

using Models.Configuration;

namespace SealCode.Security;

/// <summary>Identity claims obtained from a Google-signed ID token.</summary>
internal sealed record GoogleIdentity(string Subject, string Email, string HostedDomain, bool EmailVerified);

/// <summary>Verifies Google ID tokens before an admin session is issued.</summary>
internal interface IGoogleIdentityVerifier
{
    Task<GoogleIdentity?> VerifyAsync(string credential, string clientId);
}

/// <summary>Uses Google's signing keys and the configured web client audience.</summary>
internal sealed class GoogleIdentityVerifier : IGoogleIdentityVerifier
{
    public async Task<GoogleIdentity?> VerifyAsync(string credential, string clientId)
    {
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                credential,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] }).ConfigureAwait(false);
            return new GoogleIdentity(payload.Subject, payload.Email, payload.HostedDomain, payload.EmailVerified);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}

/// <summary>Applies the local organization's access policy to verified Google claims.</summary>
internal static class GoogleSignInAccess
{
    public static bool IsAllowed(GoogleIdentity? identity, GoogleSignInConfiguration config)
        => identity is { EmailVerified: true }
            && !string.IsNullOrWhiteSpace(identity.Subject)
            && identity.Subject.Length <= 255
            && !string.IsNullOrWhiteSpace(identity.Email)
            && !string.IsNullOrWhiteSpace(identity.HostedDomain)
            && config.AllowedHostedDomains.Contains(identity.HostedDomain, StringComparer.OrdinalIgnoreCase);
}

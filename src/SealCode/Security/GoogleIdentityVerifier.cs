using Google.Apis.Auth;

namespace SealCode.Security;

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

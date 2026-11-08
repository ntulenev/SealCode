namespace SealCode.Security;

/// <summary>Verifies Google ID tokens before an admin session is issued.</summary>
internal interface IGoogleIdentityVerifier
{
    Task<GoogleIdentity?> VerifyAsync(string credential, string clientId);
}

namespace SealCode.Routing;

/// <summary>Supplies a Google ID token to the administrator sign-in endpoint.</summary>
/// <param name="Credential">The Google-signed ID token.</param>
internal sealed record GoogleLoginRequest(string Credential);

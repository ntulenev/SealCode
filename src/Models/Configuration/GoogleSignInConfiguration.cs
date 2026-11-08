namespace Models.Configuration;

/// <summary>Controls which Google-hosted organizations may sign in as ordinary admins.</summary>
public sealed class GoogleSignInConfiguration
{
    /// <summary>Web application OAuth client ID. Omit to disable Google sign-in.</summary>
    public string? ClientId { get; init; }

    /// <summary>Exact hosted domains accepted from verified Google ID tokens.</summary>
    public string[] AllowedHostedDomains { get; init; } = [];
}

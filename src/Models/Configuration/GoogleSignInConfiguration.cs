namespace Models.Configuration;

/// <summary>Controls which Google organizations and individual accounts may sign in as ordinary admins.</summary>
public sealed class GoogleSignInConfiguration
{
    /// <summary>Web application OAuth client ID. Omit to disable Google sign-in.</summary>
    public string? ClientId { get; init; }

    /// <summary>Exact hosted domains accepted from verified Google ID tokens.</summary>
    public string[] AllowedHostedDomains { get; init; } = [];

    /// <summary>Exact email addresses accepted from verified Google ID tokens, regardless of hosted domain.</summary>
    /// <remarks>
    /// Matches full addresses without regard to case. An email match permits sign-in even
    /// without a Workspace hosted-domain claim. Defaults to an empty list.
    /// </remarks>
    public string[] AllowedEmails { get; init; } = [];
}

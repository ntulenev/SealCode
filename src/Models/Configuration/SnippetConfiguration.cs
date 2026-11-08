using System.ComponentModel.DataAnnotations;

namespace Models.Configuration;

/// <summary>
/// Controls availability of the private snippet library.
/// </summary>
public sealed class SnippetConfiguration
{
    /// <summary>Gets the persistence provider: File (default) or PostgreSql.</summary>
    public string Provider { get; init; } = "File";

    /// <summary>Gets the additional startup attempts for transient PostgreSQL failures.</summary>
    [Range(0, 10)]
    public int StartupRetryCount { get; init; } = 3;

    /// <summary>Gets the delay in seconds between PostgreSQL startup retries.</summary>
    [Range(0, 60)]
    public int StartupRetryDelaySeconds { get; init; } = 2;

    /// <summary>
    /// Gets whether the private library is enabled and loaded during startup.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets whether access is restricted to super administrators.
    /// </summary>
    public bool SuperAdminsOnly { get; init; } = true;
}

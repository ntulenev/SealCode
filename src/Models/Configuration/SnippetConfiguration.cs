namespace Models.Configuration;

/// <summary>
/// Controls availability of the private snippet library.
/// </summary>
public sealed class SnippetConfiguration
{
    /// <summary>
    /// Gets whether the library is loaded once during application startup.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets whether access is restricted to super administrators.
    /// </summary>
    public bool SuperAdminsOnly { get; init; } = true;
}

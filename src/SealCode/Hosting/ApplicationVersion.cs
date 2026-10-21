using System.Reflection;

namespace SealCode.Hosting;

/// <summary>
/// Extracts public version metadata from the application assembly.
/// </summary>
internal static class ApplicationVersion
{
    /// <summary>
    /// Gets the version, commit and full informational version.
    /// </summary>
    /// <param name="assembly">The assembly containing application version metadata.</param>
    /// <returns>The public version, optional commit and complete informational version.</returns>
    public static VersionMetadata From(Assembly assembly)
    {
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "development";
        var parts = informationalVersion.Split('+', 2);
        return new(parts[0], parts.Length > 1 ? parts[1].Split('.').Last() : null, informationalVersion);
    }
}

/// <summary>
/// Describes the running application build.
/// </summary>
/// <param name="Version">The application version without build metadata.</param>
/// <param name="Commit">The last build metadata segment, or null when no commit metadata exists.</param>
/// <param name="InformationalVersion">The complete assembly informational version.</param>
internal sealed record VersionMetadata(string Version, string? Commit, string InformationalVersion);

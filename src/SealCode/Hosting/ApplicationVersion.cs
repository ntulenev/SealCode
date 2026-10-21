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
internal sealed record VersionMetadata(string Version, string? Commit, string InformationalVersion);

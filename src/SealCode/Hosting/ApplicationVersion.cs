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

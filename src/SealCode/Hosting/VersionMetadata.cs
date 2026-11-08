namespace SealCode.Hosting;

/// <summary>
/// Describes the running application build.
/// </summary>
/// <param name="Version">The application version without build metadata.</param>
/// <param name="Commit">The last build metadata segment, or null when no commit metadata exists.</param>
/// <param name="InformationalVersion">The complete assembly informational version.</param>
internal sealed record VersionMetadata(string Version, string? Commit, string InformationalVersion);

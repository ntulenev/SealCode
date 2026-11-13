namespace Models;

/// <summary>
/// Validates room languages against configuration.
/// </summary>
public interface ILanguageValidator
{
    /// <summary>
    /// Checks whether the provided language is valid.
    /// </summary>
    /// <param name="language">The room language.</param>
    /// <returns>True when valid; otherwise false.</returns>
    bool IsValid(RoomLanguage language);

    /// <summary>
    /// Normalized list of valid languages.
    /// </summary>
    IReadOnlyList<string> Languages { get; }

    /// <summary>Gets the preferred configured language for new rooms.</summary>
    string DefaultLanguage => Languages.Contains("csharp", StringComparer.OrdinalIgnoreCase)
        ? "csharp" : Languages[0];
}

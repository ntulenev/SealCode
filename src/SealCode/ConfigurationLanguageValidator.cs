using System.Collections.Frozen;

using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

namespace SealCode;

/// <summary>
/// Validates room languages using application configuration.
/// </summary>
#pragma warning disable CA1812 // This class is instantiated by dependency injection.
internal sealed class ConfigurationLanguageValidator : ILanguageValidator
#pragma warning restore CA1812
{
    public ConfigurationLanguageValidator(IOptions<ApplicationConfiguration> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalizedLanguages = settings.Value.Languages
            .Where(static language => !string.IsNullOrWhiteSpace(language))
            .Select(static language => new RoomLanguage(language).Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Languages = normalizedLanguages;
        _languages = normalizedLanguages.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; }

    /// <inheritdoc />
    public bool IsValid(RoomLanguage language) => _languages.Contains(language.Value);

    private readonly FrozenSet<string> _languages;
}

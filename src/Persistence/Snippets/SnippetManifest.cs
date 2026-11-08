using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Persistence.Snippets;

/// <summary>
/// Describes the versioned file manifest independently of the domain and HTTP contracts.
/// </summary>
/// <param name="Version">The supported manifest format version.</param>
/// <param name="Groups">The persisted language groups.</param>
/// <param name="Schema">The optional JSON Schema reference used by editors.</param>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by System.Text.Json when reading the private file manifest.")]
internal sealed record SnippetManifest(
    int Version,
    SnippetGroupRecord[] Groups,
    [property: JsonPropertyName("$schema")] string? Schema = null);

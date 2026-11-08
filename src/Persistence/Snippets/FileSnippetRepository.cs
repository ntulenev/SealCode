using System.Text.Json;

using Abstractions;

using Models.Snippets;

namespace Persistence.Snippets;

/// <summary>
/// Loads a private JSON manifest and Markdown files into validated domain models.
/// </summary>
public sealed class FileSnippetRepository : ISnippetRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileSnippetRepository"/> class.
    /// </summary>
    /// <param name="directory">The private directory containing the manifest and Markdown files.</param>
    /// <exception cref="ArgumentNullException">Thrown when the directory is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the directory is blank or invalid.</exception>
    public FileSnippetRepository(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = Path.GetFullPath(directory);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidDataException">Thrown when persisted data violates the manifest or domain invariants.</exception>
    /// <exception cref="IOException">Thrown when a manifest or Markdown file cannot be read.</exception>
    public async Task<SnippetLibrary> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var json = await File.ReadAllTextAsync(
                Path.Combine(_directory, "catalog.json"), cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<SnippetManifest>(json, _serializerOptions);
            if (manifest is null || manifest.Version != 1
                || manifest.Groups is null || manifest.Groups.Length == 0)
            {
                throw new InvalidDataException("Snippet catalog must have version 1 and at least one group.");
            }

            var categories = new List<SnippetCategory>();
            foreach (var group in manifest.Groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (group is null || group.Questions is null)
                {
                    throw new InvalidDataException("Snippet groups and question lists cannot be null.");
                }
                var questions = new List<SnippetQuestion>();
                foreach (var question in group.Questions)
                {
                    if (question is null)
                    {
                        throw new InvalidDataException("Snippet questions cannot be null.");
                    }
                    var path = GetMarkdownPath(question.File);
                    var markdown = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                    questions.Add(new(question.Number, question.Topic, question.Description, markdown));
                }
                categories.Add(new(new SnippetLanguage(group.Language), group.Title, questions));
            }
            return new(categories);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The snippet manifest is invalid JSON or has an unsupported shape.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The snippet library violates domain invariants.", exception);
        }
    }

    private string GetMarkdownPath(string file)
    {
        // Persistence filenames never become domain identifiers or transport fields.
        if (string.IsNullOrWhiteSpace(file)
            || file.IndexOfAny(['/', '\\', ':']) >= 0
            || !file.EndsWith(".md", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Snippet files must be direct local .md filenames.");
        }
        return Path.Combine(_directory, file);
    }

    private readonly string _directory;
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };
}

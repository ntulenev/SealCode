using System.Text.Json;

using Persistence.Snippets;

namespace Persistence.Tests;

public sealed class FileSnippetRepositoryTests : IDisposable
{
    [Fact]
    public async Task VersionTwoLoadsAnOrderedProgressionAndLegacySingleFilesAsync()
    {
        WriteProgression();
        var library = await new FileSnippetRepository(_directory).LoadAsync(CancellationToken.None);
        var questions = library.Categories[0].Questions;
        Assert.Equal(2, questions[0].Steps.Count);
        Assert.Equal(["Original", "What if?"], questions[0].Steps.Select(step => step.Title));
        Assert.Equal("  First code\n", questions[0].Markdown);
        Assert.Equal("Second code", questions[0].Steps[1].Markdown);
        Assert.Single(questions[1].Steps);
    }

    [Fact]
    public async Task MissingFollowUpFileRejectsTheWholeProgressionAsync()
    {
        WriteProgression();
        File.Delete(Path.Combine(_directory, "second.md"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => new FileSnippetRepository(_directory).LoadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(1, 2, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(2, 1, 3)]
    [InlineData(2, 0, 1)]
    [InlineData(2, 2, 3)]
    public async Task InvalidVersionOrProgressionNumbersRejectTheWholeCatalogAsync(int version, int first, int second)
    {
        WriteProgression(version, first, second);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileSnippetRepository(_directory).LoadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData(/*lang=json*/ """[{"number":1,"title":"Original","file":"../first.md"}]""")]
    [InlineData(/*lang=json*/ """[{"number":1,"file":"first.md"}]""")]
    public async Task EmptyNullOrUnsafeProgressionsAreRejectedAsync(string steps)
    {
        WriteProgression();
        var manifest = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            await File.ReadAllTextAsync(Path.Combine(_directory, "catalog.json")))!;
        manifest["groups"]![0]!["questions"]![0]!["steps"] = System.Text.Json.Nodes.JsonNode.Parse(steps);
        await File.WriteAllTextAsync(Path.Combine(_directory, "catalog.json"), manifest.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileSnippetRepository(_directory).LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AQuestionCannotCombineALegacyFileWithAProgressionAsync()
    {
        WriteProgression();
        var path = Path.Combine(_directory, "catalog.json");
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        manifest["groups"]![0]!["questions"]![0]!["file"] = "first.md";
        await File.WriteAllTextAsync(path, manifest.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileSnippetRepository(_directory).LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadsDomainModelsWithoutApplyingLanguageOrRolePolicyAsync()
    {
        WriteCatalog("question.md");
        var repository = new FileSnippetRepository(_directory);
        var library = await repository.LoadAsync(CancellationToken.None);
        File.Delete(Path.Combine(_directory, "question.md"));

        Assert.Equal(["general", "csharp"], library.Categories.Select(category => category.Language.Value));
        Assert.Equal("A question", library.Categories[0].Questions[0].Markdown);
        Assert.Equal("Design principles", library.Categories[0].Questions[0].Description);
    }

    [Theory]
    [InlineData("../question.md")]
    [InlineData("..\\question.md")]
    [InlineData("C:question.md")]
    [InlineData("/question.md")]
    [InlineData("question.txt")]
    public async Task RejectsPathsOutsideThePrivateFolderAsync(string file)
    {
        WriteCatalog(file);
        var repository = new FileSnippetRepository(_directory);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MissingFileFailsTheLoadInsteadOfReturningAPartialLibraryAsync()
    {
        WriteCatalog("missing.md");
        var repository = new FileSnippetRepository(_directory);

        await Assert.ThrowsAsync<FileNotFoundException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")] // Intentionally malformed JSON; keep this string unannotated.
    [InlineData(/*lang=json*/ """{"version":2,"groups":[]}""")]
    [InlineData(/*lang=json*/ """{"version":1,"groups":[]}""")]
    [InlineData(/*lang=json*/ """{"version":1,"groups":[null]}""")]
    [InlineData(/*lang=json*/ """{"version":1,"groups":[{"language":"sql","title":"SQL"}]}""")]
    [InlineData(/*lang=json*/ """{"version":1,"groups":[{"language":"sql","title":"SQL","questions":[{"number":1,"topic":"Topic","file":"question.md"}]}]}""")]
    [InlineData(/*lang=json*/ """{"version":1,"groups":null}""")]
    [InlineData(/*lang=json*/ """{"version":1,"groups":[{"language":"sql","title":"SQL","questions":null}]}""")]
    [InlineData(/*lang=json*/ """{"version":1,"groups":[{"language":"sql","title":"SQL","questions":[null]}]}""")]
    [InlineData(/*lang=json*/ """{"version":1,"groups":[{"language":"sql","title":"SQL","questions":[]}]}""")]
    [InlineData(/*lang=json*/ """{"version":1,"groups":[],"unexpected":true}""")]
    public async Task RejectsMalformedAndUnsupportedManifestsAsync(string json)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "catalog.json"), json);
        var repository = new FileSnippetRepository(_directory);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidDomainDataIsReportedAsAPersistenceErrorAsync(bool duplicate)
    {
        WriteCatalog("question.md", duplicate);
        if (!duplicate)
        {
            await File.WriteAllTextAsync(Path.Combine(_directory, "question.md"), " ");
        }
        var repository = new FileSnippetRepository(_directory);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync(CancellationToken.None));
        Assert.IsType<ArgumentException>(error.InnerException, exactMatch: false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BlankDescriptionIsReportedAsAPersistenceErrorAsync(string? description)
    {
        WriteCatalog("question.md", description: description);
        var repository = new FileSnippetRepository(_directory);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task PublicBoundariesRejectBlankDirectoriesAndHonorCancellationBeforeIOAsync()
    {
        Assert.Throws<ArgumentNullException>(() => new FileSnippetRepository(null!));
        Assert.Throws<ArgumentException>(() => new FileSnippetRepository(" "));
        var repository = new FileSnippetRepository(_directory);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.LoadAsync(cancellation.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private void WriteCatalog(string file, bool duplicate = false, string? description = "Design principles")
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "question.md"), "A question");
        var manifest = new
        {
            version = 1,
            groups = new[] { "general", "csharp" }.Select(language => new
            {
                language,
                title = language,
                questions = Enumerable.Repeat(new { number = 1, topic = "Architecture", description, file }, duplicate ? 2 : 1)
            })
        };
        File.WriteAllText(Path.Combine(_directory, "catalog.json"), JsonSerializer.Serialize(manifest));
    }

    private void WriteProgression(int version = 2, int first = 1, int second = 2)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "first.md"), "  First code\n");
        File.WriteAllText(Path.Combine(_directory, "second.md"), "Second code");
        File.WriteAllText(Path.Combine(_directory, "catalog.json"), JsonSerializer.Serialize(new
        {
            version,
            groups = new[] { new
            {
                language = "general", title = "General", questions = new object[]
                {
                    new { number = 1, topic = "Progression", description = "Coverage", steps = new[]
                    {
                        new { number = second, title = "What if?", file = "second.md" },
                        new { number = first, title = "Original", file = "first.md" }
                    } },
                    new { number = 2, topic = "Legacy", description = "Coverage", file = "first.md" }
                }
            } }
        }));
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sealcode-snippets-{Guid.NewGuid():N}");
}

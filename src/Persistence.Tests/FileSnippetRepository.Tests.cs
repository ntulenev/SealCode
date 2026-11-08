using System.Text.Json;

using Persistence.Snippets;

namespace Persistence.Tests;

public sealed class FileSnippetRepositoryTests : IDisposable
{
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

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sealcode-snippets-{Guid.NewGuid():N}");
}

using Transport.Models;

namespace Transport.Tests;

public sealed class SaveSnippetCategoryRequestTests
{
    [Fact]
    public void MetadataAllowsAnEmptyCategoryAndPreservesItsDisplayOrder()
    {
        var category = new SaveSnippetCategoryRequest(DateTimeOffset.UtcNow, " CSharp ", " C# ", -5).ToCategory();
        Assert.Equal("csharp", category.Language.Value);
        Assert.Equal("C#", category.Title);
        Assert.Equal(-5, category.SortOrder);
        Assert.Empty(category.Questions);
    }

    [Theory]
    [InlineData(null, "Title")]
    [InlineData("", "Title")]
    [InlineData("9sql", "Title")]
    [InlineData("bad/key", "Title")]
    [InlineData("sql language", "Title")]
    [InlineData("sql", null)]
    [InlineData("sql", " ")]
    public void InvalidMetadataIsRejected(string? language, string? title)
        => Assert.ThrowsAny<ArgumentException>(() => new SaveSnippetCategoryRequest(DateTimeOffset.UtcNow, language!, title!).ToCategory());

    [Fact]
    public void MetadataLimitsMatchTheDatabaseKeyAndEditorTitleLimits()
    {
        Assert.Throws<ArgumentException>(() => new SaveSnippetCategoryRequest(DateTimeOffset.UtcNow, new string('a', 33), "Title").ToCategory());
        Assert.Throws<ArgumentException>(() => new SaveSnippetCategoryRequest(DateTimeOffset.UtcNow, "sql", new string('t', 1001)).ToCategory());
        var category = new SaveSnippetCategoryRequest(DateTimeOffset.UtcNow, new string('a', 32), new string('t', 1000)).ToCategory();
        Assert.Equal(32, category.Language.Value.Length);
        Assert.Equal(1000, category.Title.Length);
    }
}

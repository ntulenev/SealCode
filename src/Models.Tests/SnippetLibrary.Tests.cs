using Models.Snippets;

namespace Models.Tests;

public sealed class SnippetLibraryTests
{
    [Fact]
    public void LanguageNormalizesItsIdentityAndRecognizesGeneralQuestions()
    {
        var general = new SnippetLanguage("  GeNeRaL  ");
        var csharp = new SnippetLanguage(" CSharp ");

        Assert.Equal("general", general.ToString());
        Assert.Equal(new SnippetLanguage("general"), general);
        Assert.True(general.IsGeneral);
        Assert.Equal("csharp", csharp.ToString());
        Assert.False(csharp.IsGeneral);
    }

    [Fact]
    public void ConfigurationDefaultsDisableTheLibraryAndRestrictItsAudience()
    {
        var configuration = new Models.Configuration.SnippetConfiguration();

        Assert.False(configuration.Enabled);
        Assert.True(configuration.SuperAdminsOnly);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void CategoryRejectsBlankTitles(string? title)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new SnippetCategory(new("sql"), title!, [new(1, "Topic", "Coverage", "Question")]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void QuestionRejectsNonpositiveNumbers(int number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnippetQuestion(number, "Topic", "Coverage", "Question"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void QuestionsRejectBlankText(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SnippetQuestion(1, value!, "Coverage", "Question"));
        Assert.ThrowsAny<ArgumentException>(() => new SnippetQuestion(1, "Topic", "Coverage", value!));
        Assert.ThrowsAny<ArgumentException>(() => new SnippetQuestion(1, "Topic", value!, "Question"));
        Assert.ThrowsAny<ArgumentException>(() => new SnippetLanguage(value!));
    }

    [Fact]
    public void QuestionTrimsTopicButPreservesCodeIndentation()
    {
        var question = new SnippetQuestion(1, "  Topic  ", "  Coverage  ", "  code\n  next line\n");

        Assert.Equal("Topic", question.Topic);
        Assert.Equal("Coverage", question.Description);
        Assert.Equal("  code\n  next line\n", question.Markdown);
    }

    [Fact]
    public void CategoryOwnsAnOrderedDefensiveCopy()
    {
        var first = new SnippetQuestion(1, "First", "Coverage", "One");
        var second = new SnippetQuestion(2, "Second", "Coverage", "Two");
        var input = new[] { second, first };
        var category = new SnippetCategory(new(" CSharp "), "  C#  ", input);
        input[0] = new(3, "Replacement", "Coverage", "Three");

        Assert.Equal("csharp", category.Language.Value);
        Assert.Equal("C#", category.Title);
        Assert.Equal([first, second], category.Questions);
        Assert.Throws<NotSupportedException>(() => ((IList<SnippetQuestion>)category.Questions).Clear());
    }

    [Fact]
    public void CategoryRejectsInvalidInputsAndDuplicateNumbers()
    {
        var question = new SnippetQuestion(1, "Topic", "Coverage", "Question");
        Assert.ThrowsAny<ArgumentException>(() => new SnippetCategory(default, "Title", [question]));
        Assert.ThrowsAny<ArgumentException>(() => new SnippetCategory(new("sql"), " ", [question]));
        Assert.Throws<ArgumentNullException>(() => new SnippetCategory(new("sql"), "SQL", null!));
        Assert.Throws<ArgumentException>(() => new SnippetCategory(new("sql"), "SQL", []));
        Assert.Throws<ArgumentException>(() => new SnippetCategory(new("sql"), "SQL", [null!]));
        Assert.Throws<ArgumentException>(() => new SnippetCategory(new("sql"), "SQL", [question, question]));
    }

    [Fact]
    public void LibraryRejectsNullCategoriesAndDuplicateNormalizedLanguages()
    {
        var first = Category("SQL");
        var duplicate = Category(" sql ");

        Assert.Throws<ArgumentNullException>(() => new SnippetLibrary(null!));
        Assert.Throws<ArgumentException>(() => new SnippetLibrary([null!]));
        Assert.Throws<ArgumentException>(() => new SnippetLibrary([first, duplicate]));
    }

    [Fact]
    public void LibraryFiltersLanguagesWithoutMutatingTheSource()
    {
        var general = Category("GENERAL");
        var sql = Category("sql");
        var csharp = Category("csharp");
        var input = new[] { general, sql, csharp };
        var library = new SnippetLibrary(input);
        input[0] = Category("python");

        Assert.Equal([general, csharp], library.ForLanguages([new("CSharp")]).Categories);
        Assert.Equal([general], library.ForLanguages([]).Categories);
        Assert.Equal([general, sql, csharp], library.Categories);
        Assert.Throws<NotSupportedException>(() => ((IList<SnippetCategory>)library.Categories).Clear());
        Assert.Throws<ArgumentNullException>(() => library.ForLanguages(null!));
        Assert.ThrowsAny<ArgumentException>(() => library.ForLanguages([default]));
        Assert.Empty(SnippetLibrary.Empty.Categories);
    }

    private static SnippetCategory Category(string language)
        => new(new(language), language, [new(1, "Topic", "Coverage", "Question")]);
}

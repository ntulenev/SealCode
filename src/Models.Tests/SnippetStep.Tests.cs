using Models.Snippets;

namespace Models.Tests;

public sealed class SnippetStepTests
{
    [Fact]
    public void QuestionOwnsAnOrderedImmutableProgression()
    {
        var first = new SnippetStep(1, "  Original  ", "  code\n");
        var second = new SnippetStep(2, "What if?", "Changed code");
        var input = new[] { second, first };
        var question = new SnippetQuestion(1, "Topic", "Coverage", input);
        input[0] = new(3, "Replacement", "Other code");

        Assert.Equal([first, second], question.Steps);
        Assert.Equal("Original", first.Title);
        Assert.Equal("  code\n", question.Markdown);
        Assert.Throws<NotSupportedException>(() => ((IList<SnippetStep>)question.Steps).Clear());
        var legacy = new SnippetQuestion(1, "Topic", "Coverage", "Question");
        Assert.Equal("Question", Assert.Single(legacy.Steps).Markdown);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExampleRequiresAPositiveNumber(int number)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new SnippetStep(number, "Title", "Code"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ExampleRejectsBlankText(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SnippetStep(1, value!, "Code"));
        Assert.ThrowsAny<ArgumentException>(() => new SnippetStep(1, "Title", value!));
    }

    [Fact]
    public void QuestionRejectsEmptyNullDuplicateAndNonconsecutiveExamples()
    {
        var first = new SnippetStep(1, "Original", "Code");
        Assert.Throws<ArgumentNullException>(() => new SnippetQuestion(1, "Topic", "Coverage", (IEnumerable<SnippetStep>)null!));
        Assert.Throws<ArgumentException>(() => new SnippetQuestion(1, "Topic", "Coverage", []));
        Assert.Throws<ArgumentException>(() => new SnippetQuestion(1, "Topic", "Coverage", [null!]));
        Assert.Throws<ArgumentException>(() => new SnippetQuestion(1, "Topic", "Coverage", [first, first]));
        Assert.Throws<ArgumentException>(() => new SnippetQuestion(1, "Topic", "Coverage", [first, new SnippetStep(3, "Gap", "Code")]));
        Assert.Throws<ArgumentException>(() => new SnippetQuestion(1, "Topic", "Coverage", [new SnippetStep(2, "No first", "Code")]));
    }
}

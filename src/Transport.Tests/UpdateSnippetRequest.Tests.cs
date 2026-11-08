using Transport.Models;

namespace Transport.Tests;

public sealed class UpdateSnippetRequestTests
{
    [Fact]
    public void ValidRevisionPreservesMarkdownAndAssignsConsecutivePositions()
    {
        const string markdown = "  SELECT 1;\r\n";
        var question = Valid() with
        {
            Topic = " Topic ",
            Description = " Coverage ",
            Steps = [new(" First ", markdown), new(" Next ", "SELECT 2;")]
        };
        var result = question.ToQuestion(7);
        Assert.Equal(7, result.Number);
        Assert.Equal("Topic", result.Topic);
        Assert.Equal("Coverage", result.Description);
        Assert.Equal([1, 2], result.Steps.Select(step => step.Number));
        Assert.Equal("First", result.Steps[0].Title);
        Assert.Equal(markdown, result.Markdown);
    }

    [Theory]
    [InlineData("topic-null")]
    [InlineData("topic-blank")]
    [InlineData("topic-long")]
    [InlineData("description-null")]
    [InlineData("description-blank")]
    [InlineData("description-long")]
    [InlineData("steps-null")]
    [InlineData("steps-empty")]
    [InlineData("steps-many")]
    [InlineData("step-null")]
    [InlineData("title-null")]
    [InlineData("title-blank")]
    [InlineData("title-long")]
    [InlineData("markdown-null")]
    [InlineData("markdown-blank")]
    [InlineData("markdown-long")]
    [InlineData("total-long")]
    public void InvalidRevisionsAreRejectedBeforePersistence(string invalidField)
    {
        var valid = Valid();
        var request = invalidField switch
        {
            "topic-null" => valid with { Topic = null! },
            "topic-blank" => valid with { Topic = " " },
            "topic-long" => valid with { Topic = new string('t', 1001) },
            "description-null" => valid with { Description = null! },
            "description-blank" => valid with { Description = " " },
            "description-long" => valid with { Description = new string('d', 10001) },
            "steps-null" => valid with { Steps = null! },
            "steps-empty" => valid with { Steps = [] },
            "steps-many" => valid with { Steps = [.. Enumerable.Repeat(new SnippetStepInput("Title", "Source"), 101)] },
            "step-null" => valid with { Steps = [null!] },
            "title-null" => valid with { Steps = [new(null!, "Source")] },
            "title-blank" => valid with { Steps = [new(" ", "Source")] },
            "title-long" => valid with { Steps = [new(new string('t', 1001), "Source")] },
            "markdown-null" => valid with { Steps = [new("Title", null!)] },
            "markdown-blank" => valid with { Steps = [new("Title", " ")] },
            "markdown-long" => valid with { Steps = [new("Title", new string('m', 500001))] },
            "total-long" => valid with { Steps = [.. Enumerable.Repeat(new SnippetStepInput("Title", new string('m', 500000)), 5)] },
            _ => throw new ArgumentException("Unknown test case", nameof(invalidField))
        };
        Assert.ThrowsAny<ArgumentException>(() => request.ToQuestion(1));
    }

    [Fact]
    public void LimitsAreInclusiveAndQuestionNumbersMustBePositive()
    {
        var request = Valid() with
        {
            Topic = new string('t', 1000),
            Description = new string('d', 10000),
            Steps = [.. Enumerable.Repeat(new SnippetStepInput(new string('s', 1000), new string('m', 500000)), 4)]
        };
        Assert.Equal(4, request.ToQuestion(1).Steps.Count);
        Assert.Equal(100, (Valid() with { Steps = [.. Enumerable.Repeat(new SnippetStepInput("T", "M"), 100)] }).ToQuestion(1).Steps.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid().ToQuestion(0));
    }

    private static UpdateSnippetRequest Valid() => new(DateTimeOffset.UtcNow, "Topic", "Coverage", [new("Example", "Source")]);
}

using System.Text.Json;

using Models.Snippets;

using Transport.Models;

namespace Transport.Tests;

public sealed class SnippetResponseTests
{
    [Fact]
    public void ProgressionMapsEveryExampleAndKeepsTheFirstMarkdownForCompatibility()
    {
        var question = new SnippetQuestion(1, "Topic", "Coverage",
        [
            new SnippetStep(1, "Original", "First"), new SnippetStep(2, "What if?", "Second")
        ]);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(SnippetQuestionResponse.From(question)));
        var steps = json.RootElement.GetProperty("Steps");
        Assert.Equal(2, steps.GetArrayLength());
        Assert.Equal("First", json.RootElement.GetProperty("Markdown").GetString());
        Assert.Equal(2, steps[1].GetProperty("Number").GetInt32());
        Assert.Equal("What if?", steps[1].GetProperty("Title").GetString());
        Assert.Equal("Second", steps[1].GetProperty("Markdown").GetString());
        Assert.False(steps[1].TryGetProperty("File", out _));
        Assert.Throws<ArgumentNullException>(() => SnippetStepResponse.From(null!));
    }

    [Fact]
    public void MappingPreservesTheBrowserContractWithoutSerializingDomainValueObjects()
    {
        var category = new SnippetCategory(new("csharp"), "C#", [new(1, "Topic", "Coverage", "Question")]);
        var response = SnippetCategoryResponse.From(category);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));

        Assert.Equal("csharp", json.RootElement.GetProperty("Language").GetString());
        Assert.Equal("C#", json.RootElement.GetProperty("Title").GetString());
        var question = json.RootElement.GetProperty("Questions")[0];
        Assert.Equal(1, question.GetProperty("Number").GetInt32());
        Assert.Equal("Topic", question.GetProperty("Topic").GetString());
        Assert.Equal("Coverage", question.GetProperty("Description").GetString());
        Assert.Equal("Question", question.GetProperty("Markdown").GetString());
        Assert.Equal(/*lang=json,strict*/ "{\"CanAccess\":true,\"Unavailable\":false}", JsonSerializer.Serialize(new SnippetAccessResponse(true)));
    }

    [Fact]
    public void MappingRejectsNullDomainModels()
    {
        Assert.Throws<ArgumentNullException>(() => SnippetCategoryResponse.From(null!));
        Assert.Throws<ArgumentNullException>(() => SnippetQuestionResponse.From(null!));
    }
}

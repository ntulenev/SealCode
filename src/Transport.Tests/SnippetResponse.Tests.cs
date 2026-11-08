using System.Text.Json;

using Models.Snippets;

using Transport.Models;

namespace Transport.Tests;

public sealed class SnippetResponseTests
{
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
        Assert.Equal(/*lang=json,strict*/ "{\"CanAccess\":true}", JsonSerializer.Serialize(new SnippetAccessResponse(true)));
    }

    [Fact]
    public void MappingRejectsNullDomainModels()
    {
        Assert.Throws<ArgumentNullException>(() => SnippetCategoryResponse.From(null!));
        Assert.Throws<ArgumentNullException>(() => SnippetQuestionResponse.From(null!));
    }
}

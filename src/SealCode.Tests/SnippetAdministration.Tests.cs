using Abstractions;

using Logic;

using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;
using Models.Snippets;

using Moq;

using SealCode.Hosting;
using SealCode.Mcp;
using SealCode.Security;

using Transport.Models;

namespace SealCode.Tests;

public sealed class SnippetAdministrationTests
{
    [Fact]
    public async Task HttpAndMcpPublicationRefreshTheRealCatalogBeforeReturningAsync()
    {
        var repository = new Mock<IEditableSnippetRepository>();
        var original = new SnippetQuestion(1, "Topic", "Coverage", "old");
        var snapshot = Snapshot(original, DateTimeOffset.UtcNow);
        repository.Setup(value => value.LoadIfChangedAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => snapshot);
        repository.Setup(value => value.UpdateQuestionAsync(It.IsAny<SnippetLanguage>(), It.IsAny<SnippetQuestion>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<SnippetLanguage, SnippetQuestion, DateTimeOffset, CancellationToken>((_, question, _, _) =>
                snapshot = Snapshot(question, snapshot.UpdatedAt.AddSeconds(1)))
            .ReturnsAsync(true);
        var settings = Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = ["sql"],
            Snippets = new() { Enabled = true, Provider = "PostgreSql" }
        });
        using var catalog = new SnippetCatalog(repository.Object, settings, TimeProvider.System);
        await catalog.InitializeAsync(CancellationToken.None);
        var editor = new SnippetAdministration(repository.Object, catalog, settings);
        var root = new AdminUser("Root", true);
        var admins = new CurrentAdmin(root);
        var request = new UpdateSnippetRequest(snapshot.UpdatedAt, "New topic", "New coverage", [new("Example", "new source")]);
        Assert.True(await editor.PublishAsync("sql", 1, request, CancellationToken.None));
        Assert.Equal("new source", catalog.GetSnapshot(root).Categories[0].Questions[0].Markdown);
        var next = request with { ExpectedVersion = snapshot.UpdatedAt, Steps = [new("Example", "MCP source")] };
        await SnippetMcpTools.UpdateAsync(editor, admins, "sql", 1, next, CancellationToken.None);
        Assert.Equal("MCP source", catalog.GetSnapshot(root).Categories[0].Questions[0].Markdown);
        repository.Verify(value => value.LoadIfChangedAsync(It.IsAny<DateTimeOffset?>(), CancellationToken.None), Times.Exactly(3));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task McpRejectsUnauthorizedOrDisabledEditingAsync(bool enabled, bool superAdmin, bool allowed)
    {
        var repository = new Mock<IEditableSnippetRepository>(MockBehavior.Strict);
        var catalog = new Mock<ISnippetCatalog>();
        var settings = Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = [],
            Snippets = new() { Enabled = enabled, Provider = "PostgreSql" }
        });
        var editor = new SnippetAdministration(repository.Object, catalog.Object, settings);
        var admin = new AdminUser("Admin", superAdmin);
        var admins = new CurrentAdmin(admin);
        Assert.Equal(allowed, editor.CanEdit(admins));
        if (!allowed)
        {
            await Assert.ThrowsAsync<ModelContextProtocol.McpException>(() => SnippetMcpTools.ListAsync(editor, admins, CancellationToken.None));
        }

        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConflictDoesNotRefreshCacheAndInvalidRevisionDoesNotWriteAsync()
    {
        var repository = new Mock<IEditableSnippetRepository>();
        repository.Setup(value => value.UpdateQuestionAsync(It.IsAny<SnippetLanguage>(), It.IsAny<SnippetQuestion>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var catalog = new Mock<ISnippetCatalog>(MockBehavior.Strict);
        var settings = Options.Create(new ApplicationConfiguration { AdminUsers = [], Languages = [] });
        var editor = new SnippetAdministration(repository.Object, catalog.Object, settings);
        var request = new UpdateSnippetRequest(DateTimeOffset.UtcNow, "Topic", "Coverage", [new("Example", "source")]);
        Assert.False(await editor.PublishAsync("sql", 1, request, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => editor.PublishAsync("sql", 1,
            request with { Steps = [] }, CancellationToken.None));
        repository.Verify(value => value.UpdateQuestionAsync(It.IsAny<SnippetLanguage>(), It.IsAny<SnippetQuestion>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        catalog.VerifyNoOtherCalls();
    }

    private sealed class CurrentAdmin(AdminUser current) : IAdminUserManager
    {
        public bool IsAdmin() => true;
        public bool TryGetAdminUser(out AdminUser adminUser) { adminUser = current; return true; }
        public Task<bool> TrySetCurrentUserAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TrySetGoogleCurrentUserAsync(GoogleIdentity identity) => throw new NotSupportedException();
        public Task ClearCurrentAdminUserAsync() => throw new NotSupportedException();
    }

    private static VersionedSnippetLibrary Snapshot(SnippetQuestion question, DateTimeOffset version)
        => new(version, new SnippetLibrary([new SnippetCategory(new("sql"), "SQL", [question])]));
}

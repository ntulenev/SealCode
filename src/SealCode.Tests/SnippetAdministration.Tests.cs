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

    [Fact]
    public void ConstructorRejectsMissingDependencies()
    {
        var repository = new Mock<ISnippetRepository>().Object;
        var catalog = new Mock<ISnippetCatalog>().Object;
        var settings = Options.Create(new ApplicationConfiguration { AdminUsers = [], Languages = [] });
        Assert.Throws<ArgumentNullException>(() => new SnippetAdministration(null!, catalog, settings));
        Assert.Throws<ArgumentNullException>(() => new SnippetAdministration(repository, null!, settings));
        Assert.Throws<ArgumentNullException>(() => new SnippetAdministration(repository, catalog, null!));
    }

    [Theory]
    [InlineData("File", true, true)]
    [InlineData("PostgreSql", false, true)]
    [InlineData("PostgreSql", true, false)]
    public async Task ReadOnlyProvidersAndAnonymousRequestsCannotEditAsync(string provider, bool writable, bool authenticated)
    {
        var repository = writable ? new Mock<IEditableSnippetRepository>().Object : new Mock<ISnippetRepository>().Object;
        var settings = Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = [],
            Snippets = new() { Enabled = true, Provider = provider }
        });
        var editor = new SnippetAdministration(repository, new Mock<ISnippetCatalog>().Object, settings);
        var admins = new CurrentAdmin(new("Root", true), authenticated);
        Assert.False(editor.CanEdit(admins));
        var request = new UpdateSnippetRequest(DateTimeOffset.UtcNow, "Topic", "Coverage", [new("Title", "Source")]);
        await Assert.ThrowsAsync<ModelContextProtocol.McpException>(() => SnippetMcpTools.UpdateAsync(
            editor, admins, "sql", 1, request, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationAfterCommitCannotCancelCachePublicationAsync()
    {
        using var canceled = new CancellationTokenSource();
        var repository = new Mock<IEditableSnippetRepository>();
        repository.Setup(value => value.UpdateQuestionAsync(It.IsAny<SnippetLanguage>(), It.IsAny<SnippetQuestion>(),
                It.IsAny<DateTimeOffset>(), canceled.Token))
            .Callback(canceled.Cancel).ReturnsAsync(true);
        var catalog = new Mock<ISnippetCatalog>(MockBehavior.Strict);
        catalog.Setup(value => value.RefreshAsync(CancellationToken.None)).Returns(Task.CompletedTask);
        var settings = Options.Create(new ApplicationConfiguration { AdminUsers = [], Languages = [] });
        var editor = new SnippetAdministration(repository.Object, catalog.Object, settings);
        var request = new UpdateSnippetRequest(DateTimeOffset.UtcNow, "Topic", "Coverage", [new("Title", "Source")]);
        Assert.True(await editor.PublishAsync("sql", 1, request, canceled.Token));
        Assert.True(canceled.IsCancellationRequested);
        catalog.Verify(value => value.RefreshAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task CacheFailureDoesNotReportPublicationSuccessAsync()
    {
        var repository = new Mock<IEditableSnippetRepository>();
        repository.Setup(value => value.UpdateQuestionAsync(It.IsAny<SnippetLanguage>(), It.IsAny<SnippetQuestion>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var catalog = new Mock<ISnippetCatalog>();
        catalog.Setup(value => value.RefreshAsync(CancellationToken.None)).ThrowsAsync(new IOException("Database unavailable"));
        var settings = Options.Create(new ApplicationConfiguration { AdminUsers = [], Languages = [] });
        var editor = new SnippetAdministration(repository.Object, catalog.Object, settings);
        await Assert.ThrowsAsync<IOException>(() => editor.PublishAsync("sql", 1,
            new(DateTimeOffset.UtcNow, "Topic", "Coverage", [new("Title", "Source")]), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => editor.PublishAsync("sql", 1, null!, CancellationToken.None));
    }

    private sealed class CurrentAdmin : IAdminUserManager
    {
        public CurrentAdmin(AdminUser current, bool authenticated = true)
        {
            _current = current;
            _authenticated = authenticated;
        }

        public bool IsAdmin() => _authenticated;
        public bool TryGetAdminUser(out AdminUser adminUser) { adminUser = _current; return _authenticated; }
        public Task<bool> TrySetCurrentUserAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TrySetGoogleCurrentUserAsync(GoogleIdentity identity) => throw new NotSupportedException();
        public Task ClearCurrentAdminUserAsync() => throw new NotSupportedException();

        private readonly AdminUser _current;
        private readonly bool _authenticated;
    }

    private static VersionedSnippetLibrary Snapshot(SnippetQuestion question, DateTimeOffset version)
        => new(version, new SnippetLibrary([new SnippetCategory(new("sql"), "SQL", [question])]));
}

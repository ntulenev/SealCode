using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Abstractions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Models.Snippets;

using Moq;

using Transport.Models;

namespace SealCode.Tests;

public sealed class SnippetAdministrationEndpointsTests
{
    [Theory]
    [InlineData(null, true, "PostgreSql", HttpStatusCode.Unauthorized)]
    [InlineData("Regular", true, "PostgreSql", HttpStatusCode.Forbidden)]
    [InlineData("Root", false, "PostgreSql", HttpStatusCode.NotFound)]
    [InlineData("Root", true, "File", HttpStatusCode.NotFound)]
    public async Task EveryEditorRouteEnforcesDeploymentAndRolePolicyAsync(string? user, bool enabled,
        string provider, HttpStatusCode expected)
    {
        using var factory = new EditorFactory(enabled, provider);
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (user is not null)
        {
            await LoginAsync(http, user);
        }

        await SetTokenAsync(http);
        using var access = await http.GetAsync("/admin/snippets/manage/access");
        Assert.False((await access.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("CanEdit").GetBoolean());
        foreach (var path in new[] { "/admin/snippets/manage", "/admin/snippets/manage/catalog" })
        {
            using var response = await http.GetAsync(path);
            Assert.Equal(expected, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        using var publish = await http.PostAsJsonAsync("/admin/snippets/manage/general/1", Revision(factory.Version));
        Assert.Equal(expected, publish.StatusCode);
        factory.Repository.Verify(value => value.UpdateQuestionAsync(It.IsAny<SnippetLanguage>(),
            It.IsAny<SnippetQuestion>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AuthorizedEditorPublishesRefreshesAndRejectsStaleOrInvalidRequestsAsync()
    {
        using var factory = new EditorFactory();
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await LoginAsync(http, "Root");
        using var access = await http.GetAsync("/admin/snippets/manage/access");
        Assert.True((await access.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("CanEdit").GetBoolean());
        using var page = await http.GetAsync("/admin/snippets/manage");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Preview before publishing", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var snapshot = await http.GetFromJsonAsync<JsonElement>("/admin/snippets/manage/catalog");
        Assert.Equal(factory.Version, snapshot.GetProperty("Version").GetDateTimeOffset());
        var request = Revision(factory.Version);
        http.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var unsafeRequest = await http.PostAsJsonAsync("/admin/snippets/manage/general/1", request);
        Assert.Equal(HttpStatusCode.BadRequest, unsafeRequest.StatusCode);
        await SetTokenAsync(http);
        using var invalid = await http.PostAsJsonAsync("/admin/snippets/manage/general/1", request with { Steps = [] });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var saved = await http.PostAsJsonAsync("/admin/snippets/manage/general/1", request);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var cached = await http.GetFromJsonAsync<JsonElement>("/admin/snippets/catalog");
        Assert.Equal("Published source", cached[0].GetProperty("Questions")[0].GetProperty("Markdown").GetString());
        using var stale = await http.PostAsJsonAsync("/admin/snippets/manage/general/1", request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("Catalog changed", await stale.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static UpdateSnippetRequest Revision(DateTimeOffset version)
        => new(version, "Edited topic", "Coverage", [new("Example", "Published source")]);

    private static async Task SetTokenAsync(HttpClient http)
    {
        var token = await http.GetFromJsonAsync<JsonElement>("/admin/token");
        http.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        http.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("Token").GetString());
    }

    private static async Task LoginAsync(HttpClient http, string name)
    {
        await SetTokenAsync(http);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = name, ["password"] = "editor-test-password" });
        using var response = await http.PostAsync("/admin/login", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await SetTokenAsync(http);
    }

    private sealed class EditorFactory : WebApplicationFactory<Program>
    {
        public EditorFactory(bool enabled = true, string provider = "PostgreSql")
        {
            _enabled = enabled;
            _provider = provider;
            Repository.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Library());
            Repository.Setup(value => value.LoadIfChangedAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new VersionedSnippetLibrary(Version, Library()));
            Repository.Setup(value => value.UpdateQuestionAsync(It.IsAny<SnippetLanguage>(), It.IsAny<SnippetQuestion>(),
                    It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .Returns<SnippetLanguage, SnippetQuestion, DateTimeOffset, CancellationToken>((_, question, version, _) =>
                {
                    if (version != Version)
                    {
                        return Task.FromResult(false);
                    }

                    _question = question;
                    Version = Version.AddSeconds(1);
                    return Task.FromResult(true);
                });
        }

        public Mock<IEditableSnippetRepository> Repository { get; } = new();
        public DateTimeOffset Version { get; private set; } = DateTimeOffset.UtcNow;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Snippets:Enabled"] = _enabled.ToString(),
                ["Snippets:Provider"] = _provider,
                ["AdminUsers:0:Name"] = "Root",
                ["AdminUsers:0:Password"] = "editor-test-password",
                ["AdminUsers:0:PasswordHash"] = null,
                ["AdminUsers:0:IsSuperAdmin"] = "true",
                ["AdminUsers:1:Name"] = "Regular",
                ["AdminUsers:1:Password"] = "editor-test-password",
                ["AdminUsers:1:PasswordHash"] = null,
                ["AdminUsers:1:IsSuperAdmin"] = "false"
            }));
            builder.ConfigureServices(services => services.AddSingleton<ISnippetRepository>(Repository.Object));
        }

        private SnippetLibrary Library() => new([new SnippetCategory(new("general"), "General", [_question])]);

        private readonly bool _enabled;
        private readonly string _provider;
        private SnippetQuestion _question = new(1, "Topic", "Coverage", "Original");
    }
}

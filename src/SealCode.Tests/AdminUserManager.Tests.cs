using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using System.Text;

using Models;
using Models.Configuration;

using SealCode.Security;

namespace SealCode.Tests;

public sealed class AdminUserManagerTests
{
    private const string COOKIENAME = "admin_auth";

    [Fact(DisplayName = "CtorShouldThrowWhenContextIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenContextIsNull()
    {
        var settings = CreateSettings();

        var action = () => new AdminUserManager(settings, null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenSettingsIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenSettingsIsNull()
    {
        var context = new DefaultHttpContext();

        var action = () => new AdminUserManager(null!, context);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "IsAdminShouldReturnFalseWhenCookieMissing")]
    [Trait("Category", "Unit")]
    public void IsAdminShouldReturnFalseWhenCookieMissing()
    {
        var context = new DefaultHttpContext();
        var manager = CreateManager(context);

        var result = manager.IsAdmin();

        result.Should().BeFalse();
    }

    [Fact(DisplayName = "TryGetAdminUserShouldReturnFalseWhenCookieMissing")]
    [Trait("Category", "Unit")]
    public void TryGetAdminUserShouldReturnFalseWhenCookieMissing()
    {
        var context = new DefaultHttpContext();
        var manager = CreateManager(context);

        var result = manager.TryGetAdminUser(out var user);

        result.Should().BeFalse();
        user.Should().Be(default(AdminUser));
    }

    [Fact(DisplayName = "TryGetAdminUserShouldReturnFalseWhenCookieIsWhiteSpace")]
    [Trait("Category", "Unit")]
    public void TryGetAdminUserShouldReturnFalseWhenCookieIsWhiteSpace()
    {
        var context = new DefaultHttpContext();
        var manager = CreateManager(context);
        SetCookie(context, $"{COOKIENAME}= ");

        var result = manager.TryGetAdminUser(out var user);

        result.Should().BeFalse();
        user.Should().Be(default(AdminUser));
    }

    [Fact(DisplayName = "TryGetAdminUserShouldReturnFalseWhenAdminNotFound")]
    [Trait("Category", "Unit")]
    public void TryGetAdminUserShouldReturnFalseWhenAdminNotFound()
    {
        var context = new DefaultHttpContext();
        var manager = CreateManager(context);
        SetCookie(context, $"{COOKIENAME}=unknown");

        var result = manager.TryGetAdminUser(out var user);

        result.Should().BeFalse();
        user.Should().Be(default(AdminUser));
    }

    [Fact(DisplayName = "TryGetAdminUserShouldReturnTrueWhenAdminFound")]
    [Trait("Category", "Unit")]
    public void TryGetAdminUserShouldReturnTrueWhenAdminFound()
    {
        var context = new DefaultHttpContext();
        var manager = CreateManager(context);
        SetCookie(context, $"{COOKIENAME}=ADMIN");

        var result = manager.TryGetAdminUser(out var user);

        result.Should().BeTrue();
        user.Name.Should().Be("Admin");
        user.IsSuperAdmin.Should().BeFalse();
    }

    [Fact(DisplayName = "TrySetCurrentUserShouldReturnFalseWhenFormMissingCredentials")]
    [Trait("Category", "Unit")]
    public async Task TrySetCurrentUserShouldReturnFalseWhenFormMissingCredentials()
    {
        var context = new DefaultHttpContext();
        var manager = CreateManager(context);
        SetForm(context, string.Empty);

        var result = await manager.TrySetCurrentUserAsync(CancellationToken.None);

        result.Should().BeFalse();
        context.Response.Headers.TryGetValue("Set-Cookie", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "TrySetCurrentUserShouldReturnFalseWhenPasswordIsInvalid")]
    [Trait("Category", "Unit")]
    public async Task TrySetCurrentUserShouldReturnFalseWhenPasswordIsInvalid()
    {
        var context = new DefaultHttpContext();
        var manager = CreateManager(context);
        SetForm(context, "name=Admin&password=wrong");

        var result = await manager.TrySetCurrentUserAsync(CancellationToken.None);

        result.Should().BeFalse();
        context.Response.Headers.TryGetValue("Set-Cookie", out _).Should().BeFalse();
    }

    [Fact(DisplayName = "TrySetCurrentUserShouldAppendCookieWhenValid")]
    [Trait("Category", "Unit")]
    public async Task TrySetCurrentUserShouldAppendCookieWhenValid()
    {
        var context = new DefaultHttpContext();
        var manager = CreateManager(context);
        SetForm(context, "name=Admin&password=pass1");

        var result = await manager.TrySetCurrentUserAsync(CancellationToken.None);

        result.Should().BeTrue();
        context.Response.Headers.TryGetValue("Set-Cookie", out var headerValues).Should().BeTrue();
        headerValues.ToString().Should().Contain($"{COOKIENAME}=Admin");
    }

    [Fact(DisplayName = "ClearCurrentAdminUserShouldDeleteCookie")]
    [Trait("Category", "Unit")]
    public void ClearCurrentAdminUserShouldDeleteCookie()
    {
        var context = new DefaultHttpContext();
        var manager = CreateManager(context);

        manager.ClearCurrentAdminUser();

        context.Response.Headers.TryGetValue("Set-Cookie", out var headerValues).Should().BeTrue();
        headerValues.ToString().Should().Contain($"{COOKIENAME}=");
    }

    [Fact(DisplayName = "TryGetAdminUserShouldMapSuperAdminFlag")]
    [Trait("Category", "Unit")]
    public void TryGetAdminUserShouldMapSuperAdminFlag()
    {
        var context = new DefaultHttpContext();
        var manager = CreateManager(context);
        SetCookie(context, $"{COOKIENAME}=root");

        var result = manager.TryGetAdminUser(out var user);

        result.Should().BeTrue();
        user.Name.Should().Be("Root");
        user.IsSuperAdmin.Should().BeTrue();
    }

    private static IOptions<ApplicationConfiguration> CreateSettings() => Options.Create(new ApplicationConfiguration
    {
        AdminUsers =
        [
            new AdminUserConfiguration { Name = "Admin", Password = "pass1" },
            new AdminUserConfiguration { Name = "Root", Password = "pass2", IsSuperAdmin = true }
        ],
        Languages = ["csharp", "sql"],
        MaxUsersPerRoom = 3
    });

    private static AdminUserManager CreateManager(HttpContext context)
        => new(CreateSettings(), context);

    private static void SetForm(DefaultHttpContext context, string formBody)
    {
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(formBody));
        context.Request.ContentLength = context.Request.Body.Length;
    }

    private static void SetCookie(DefaultHttpContext context, string cookieHeader)
    {
        var parts = cookieHeader.Split('=', 2, StringSplitOptions.TrimEntries);
        var name = parts.Length > 0 ? parts[0] : string.Empty;
        var value = parts.Length > 1 ? parts[1] : string.Empty;
        var cookies = new TestRequestCookieCollection(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [name] = value
        });

        context.Features.Set<IRequestCookiesFeature>(new TestRequestCookiesFeature(cookies));
    }

    private sealed class TestRequestCookiesFeature : IRequestCookiesFeature
    {
        public TestRequestCookiesFeature(IRequestCookieCollection cookies)
        {
            Cookies = cookies;
        }

        public IRequestCookieCollection Cookies { get; set; }
    }

    private sealed class TestRequestCookieCollection : IRequestCookieCollection
    {
        private readonly Dictionary<string, string> _cookies;

        public TestRequestCookieCollection(Dictionary<string, string> cookies)
        {
            _cookies = cookies;
        }

        public int Count => _cookies.Count;

        public ICollection<string> Keys => _cookies.Keys;

        public string? this[string key] => _cookies.TryGetValue(key, out var value) ? value : null;

        public bool ContainsKey(string key) => _cookies.ContainsKey(key);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _cookies.GetEnumerator();

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value)
        {
            if (_cookies.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

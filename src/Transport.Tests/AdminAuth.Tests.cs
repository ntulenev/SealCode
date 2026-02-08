using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Diagnostics.CodeAnalysis;

using Models.Configuration;

using Transport.Admin;

namespace Transport.Tests;

public sealed class AdminAuthTests
{
    [Fact(DisplayName = "IsAdminShouldThrowWhenContextIsNull")]
    [Trait("Category", "Unit")]
    public void IsAdminShouldThrowWhenContextIsNull()
    {
        var settings = CreateSettings();

        var action = () => AdminAuth.IsAdmin(null!, settings);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "IsAdminShouldThrowWhenSettingsIsNull")]
    [Trait("Category", "Unit")]
    public void IsAdminShouldThrowWhenSettingsIsNull()
    {
        var context = new DefaultHttpContext();

        var action = () => AdminAuth.IsAdmin(context, null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "IsAdminShouldReturnFalseWhenCookieMissing")]
    [Trait("Category", "Unit")]
    public void IsAdminShouldReturnFalseWhenCookieMissing()
    {
        var context = new DefaultHttpContext();
        var settings = CreateSettings();

        var result = AdminAuth.IsAdmin(context, settings);

        result.Should().BeFalse();
    }

    [Fact(DisplayName = "TryGetAdminNameShouldReturnFalseWhenCookieMissing")]
    [Trait("Category", "Unit")]
    public void TryGetAdminNameShouldReturnFalseWhenCookieMissing()
    {
        var context = new DefaultHttpContext();
        var settings = CreateSettings();

        var result = AdminAuth.TryGetAdminName(context, settings, out var name);

        result.Should().BeFalse();
        name.Should().Be(string.Empty);
    }

    [Fact(DisplayName = "TryGetAdminNameShouldReturnFalseWhenCookieIsWhiteSpace")]
    [Trait("Category", "Unit")]
    public void TryGetAdminNameShouldReturnFalseWhenCookieIsWhiteSpace()
    {
        var context = new DefaultHttpContext();
        var settings = CreateSettings();
        SetCookie(context, $"{AdminAuth.COOKIENAME}= ");

        var result = AdminAuth.TryGetAdminName(context, settings, out var name);

        result.Should().BeFalse();
        name.Should().Be(string.Empty);
    }

    [Fact(DisplayName = "TryGetAdminNameShouldReturnFalseWhenAdminNotFound")]
    [Trait("Category", "Unit")]
    public void TryGetAdminNameShouldReturnFalseWhenAdminNotFound()
    {
        var context = new DefaultHttpContext();
        var settings = CreateSettings();
        SetCookie(context, $"{AdminAuth.COOKIENAME}=unknown");

        var result = AdminAuth.TryGetAdminName(context, settings, out var name);

        result.Should().BeFalse();
        name.Should().Be(string.Empty);
    }

    [Fact(DisplayName = "TryGetAdminNameShouldReturnTrueWhenAdminFound")]
    [Trait("Category", "Unit")]
    public void TryGetAdminNameShouldReturnTrueWhenAdminFound()
    {
        var context = new DefaultHttpContext();
        var settings = CreateSettings();
        SetCookie(context, $"{AdminAuth.COOKIENAME}=ADMIN");

        var result = AdminAuth.TryGetAdminName(context, settings, out var name);

        result.Should().BeTrue();
        name.Should().Be("Admin");
    }

    private static ApplicationConfiguration CreateSettings() => new()
    {
        AdminUsers =
        [
            new AdminUserConfiguration { Name = "Admin", Password = "pass1" },
            new AdminUserConfiguration { Name = "Root", Password = "pass2" }
        ],
        Languages = ["csharp", "sql"],
        MaxUsersPerRoom = 3
    };

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

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;

using Models.Configuration;

using Moq;

namespace SealCode.Tests;

public sealed class ConfigurationValidationTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(10, 60, true)]
    [InlineData(-1, 2, false)]
    [InlineData(11, 2, false)]
    [InlineData(3, -1, false)]
    [InlineData(3, 61, false)]
    public void SnippetRetrySettingsHaveBoundedCountsAndDelays(int retries, int delay, bool valid)
    {
        var settings = new ApplicationConfiguration
        {
            AdminUsers = [new() { Name = "Admin", Password = "valid" }],
            Languages = ["csharp"],
            MaxUsersPerRoom = 3,
            Snippets = new() { StartupRetryCount = retries, StartupRetryDelaySeconds = delay }
        };
        Assert.Equal(valid, Validator("Development").Validate(null, settings).Succeeded);
    }

    [Theory]
    [InlineData("File", true)]
    [InlineData("PostgreSql", true)]
    [InlineData("postgresql", true)]
    [InlineData("Unknown", false)]
    [InlineData(null, false)]
    public void SnippetProviderMustBeRecognized(string? provider, bool valid)
    {
        var settings = new ApplicationConfiguration
        {
            AdminUsers = [new() { Name = "Admin", Password = "valid" }],
            Languages = ["csharp"],
            MaxUsersPerRoom = 3,
            Snippets = new() { Provider = provider! }
        };
        Assert.Equal(valid, Validator("Development").Validate(null, settings).Succeeded);
    }

    [Fact]
    public void SnippetConfigurationValidationRejectsANullSection()
    {
        var configuration = new ApplicationConfiguration
        {
            AdminUsers = [new() { Name = "Admin", Password = "valid" }],
            Languages = ["csharp"],
            MaxUsersPerRoom = 3,
            Snippets = null!
        };

        Assert.True(Validator("Development").Validate(null, configuration).Failed);
    }

    private static ApplicationConfiguration Settings(
        AdminUserConfiguration[] admins,
        string[]? languages = null,
        GoogleSignInConfiguration? google = null) => new()
        {
            AdminUsers = admins,
            Languages = languages ?? ["csharp"],
            GoogleSignIn = google ?? new(),
            MaxUsersPerRoom = 3
        };
    private static ApplicationConfigurationValidator Validator(string environment = "Production")
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(x => x.EnvironmentName).Returns(environment);
        return new(env.Object);
    }
    [Fact]
    public void ProductionRequiresHashAndRejectsLegacySecrets()
    {
        Assert.True(Validator().Validate(null, Settings([new() { Name = "Admin", Password = "legacy" }])).Failed);
        var user = new AdminUserConfiguration { Name = "Admin" };
        var hash = new PasswordHasher<AdminUserConfiguration>().HashPassword(user, "test password");
        Assert.True(Validator().Validate(null, Settings([new() { Name = "Admin", PasswordHash = hash }])).Succeeded);
        Assert.True(Validator().Validate(null, Settings([new() { Name = "Admin", PasswordHash = "invalid" }])).Failed);
    }
    [Fact]
    public void McpTokensAreOptionalAndMustBeUniqueAndSuitableForBearerHeaders()
    {
        Assert.True(Validator("Development").Validate(null, Settings([
            new() { Name = "Admin", Password = "valid", McpToken = "" }])).Succeeded);
        var token = new string('a', 32);
        Assert.True(Validator("Development").Validate(null, Settings([
            new() { Name = "Admin", Password = "valid", McpToken = token }])).Succeeded);
        Assert.True(Validator("Development").Validate(null, Settings([
            new() { Name = "Admin", Password = "valid", McpToken = token },
            new() { Name = "Other", Password = "valid", McpToken = token }])).Failed);
        foreach (var valid in new[] { token + "-._~+/", token + "==" })
        {
            Assert.True(Validator("Development").Validate(null, Settings([
                new() { Name = "Admin", Password = "valid", McpToken = valid }])).Succeeded);
        }
        foreach (var invalid in new[]
        {
            "short", " ", new string('a', 1025), token + "\n", token + "é",
            token + ",", token + "\"", token + ":", token + ";", token + "=a", new string('=', 32)
        })
        {
            Assert.True(Validator("Development").Validate(null, Settings([
                new() { Name = "Admin", Password = "valid", McpToken = invalid }])).Failed);
        }
    }

    [Fact]
    public void NestedAndNormalizedSettingsAreValidated()
    {
        Assert.True(Validator("Development").Validate(null, Settings([null!])).Failed);
        Assert.True(Validator("Development").Validate(null, Settings([new() { Name = "A", Password = "valid" }])).Failed);
        Assert.True(Validator("Development").Validate(null, Settings([new() { Name = "Admin", Password = "valid" }, new() { Name = " admin ", Password = "valid" }])).Failed);
        Assert.True(Validator("Development").Validate(null, Settings([new() { Name = "Admin", Password = "valid" }], [" ", "\t"])).Failed);
        Assert.True(Validator("Development").Validate(null, Settings([new() { Name = "google:123", Password = "valid" }])).Failed);
    }

    [Fact]
    public void GoogleSignInRequiresClientIdAndUniqueHostedDomainsOrEmails()
    {
        var admins = new[] { new AdminUserConfiguration { Name = "Admin", Password = "valid" } };
        Assert.True(Validator("Development").Validate(null, Settings(admins)).Succeeded);
        Assert.True(Validator("Development").Validate(null, Settings(admins, google: new()
        {
            ClientId = "client.apps.googleusercontent.com",
            AllowedHostedDomains = ["example.org"]
        })).Succeeded);
        Assert.True(Validator("Development").Validate(null, Settings(admins, google: new()
        {
            ClientId = "client.apps.googleusercontent.com"
        })).Failed);
        Assert.True(Validator("Development").Validate(null, Settings(admins, google: new()
        {
            AllowedHostedDomains = ["example.org"]
        })).Failed);
        Assert.True(Validator("Development").Validate(null, Settings(admins, google: new()
        {
            ClientId = "client.apps.googleusercontent.com",
            AllowedHostedDomains = ["example.org", "EXAMPLE.ORG"]
        })).Failed);
        Assert.True(Validator("Development").Validate(null, Settings(admins, google: new()
        {
            ClientId = "client.apps.googleusercontent.com",
            AllowedHostedDomains = ["not a domain"]
        })).Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoogleSignInAcceptsEmailOnlyOrCombinedAllowlists(bool includeDomain)
    {
        Assert.True(Validator("Development").Validate(null, Settings([], google: new()
        {
            ClientId = "client.apps.googleusercontent.com",
            AllowedHostedDomains = includeDomain ? ["example.org"] : [],
            AllowedEmails = ["user123@gmail.com", "other@example.org"]
        })).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-an-email")]
    [InlineData("user123@gmail.com ")]
    [InlineData(" user123@gmail.com")]
    [InlineData("Display Name <user123@gmail.com>")]
    [InlineData("*@gmail.com")]
    [InlineData("user@*")]
    [InlineData("user@gmail.com\n")]
    public void GoogleSignInRejectsMalformedEmailEntriesEvenWithAllowedDomain(string? email)
    {
        Assert.True(Validator("Development").Validate(null, Settings([], google: new()
        {
            ClientId = "client.apps.googleusercontent.com",
            AllowedHostedDomains = ["example.org"],
            AllowedEmails = [email!]
        })).Failed);
    }

    [Fact]
    public void GoogleSignInRejectsDuplicateEmailsMissingClientIdAndNullAllowlists()
    {
        GoogleSignInConfiguration[] invalid =
        [
            new() { AllowedEmails = ["user123@gmail.com"] },
            new() { ClientId = "invalid", AllowedEmails = ["user123@gmail.com"] },
            new()
            {
                ClientId = "client.apps.googleusercontent.com",
                AllowedEmails = ["user123@gmail.com", "USER123@GMAIL.COM"]
            },
            new() { AllowedEmails = null! },
            new() { AllowedHostedDomains = null! }
        ];
        foreach (var google in invalid)
        {
            Assert.True(Validator("Development").Validate(null, Settings([], google: google)).Failed);
        }
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;

using Models.Configuration;

using Moq;

namespace SealCode.Tests;

public sealed class ConfigurationValidationTests
{
    private static ApplicationConfiguration Settings(
        AdminUserConfiguration[] admins,
        string[]? languages = null) => new()
        {
            AdminUsers = admins,
            Languages = languages ?? ["csharp"],
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
    }
}

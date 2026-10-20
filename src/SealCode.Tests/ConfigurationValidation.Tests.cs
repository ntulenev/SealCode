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
    public void NestedAndNormalizedSettingsAreValidated()
    {
        Assert.True(Validator("Development").Validate(null, Settings([null!])).Failed);
        Assert.True(Validator("Development").Validate(null, Settings([new() { Name = "A", Password = "valid" }])).Failed);
        Assert.True(Validator("Development").Validate(null, Settings([new() { Name = "Admin", Password = "valid" }, new() { Name = " admin ", Password = "valid" }])).Failed);
        Assert.True(Validator("Development").Validate(null, Settings([new() { Name = "Admin", Password = "valid" }], [" ", "\t"])).Failed);
    }
}

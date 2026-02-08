using FluentAssertions;

using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

namespace SealCode.Tests;

public sealed class ConfigurationLanguageValidatorTests
{
    [Fact(DisplayName = "CtorShouldThrowWhenSettingsIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenSettingsIsNull()
    {
        var action = () => new ConfigurationLanguageValidator(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "LanguagesShouldReturnNormalizedDistinctValues")]
    [Trait("Category", "Unit")]
    public void LanguagesShouldReturnNormalizedDistinctValues()
    {
        var settings = Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [new AdminUserConfiguration { Name = "Admin", Password = "pass1" }],
            Languages = [" CSharp ", "sql", "SQL", "  ", "csharp"],
            MaxUsersPerRoom = 3
        });

        var validator = new ConfigurationLanguageValidator(settings);

        validator.Languages.Should().Equal("csharp", "sql");
    }

    [Fact(DisplayName = "IsValidShouldReturnTrueForConfiguredLanguage")]
    [Trait("Category", "Unit")]
    public void IsValidShouldReturnTrueForConfiguredLanguage()
    {
        var validator = CreateValidator();

        var result = validator.IsValid(new RoomLanguage("sql"));

        result.Should().BeTrue();
    }

    [Fact(DisplayName = "IsValidShouldReturnFalseForUnknownLanguage")]
    [Trait("Category", "Unit")]
    public void IsValidShouldReturnFalseForUnknownLanguage()
    {
        var validator = CreateValidator();

        var result = validator.IsValid(new RoomLanguage("python"));

        result.Should().BeFalse();
    }

    private static ConfigurationLanguageValidator CreateValidator()
    {
        var settings = Options.Create(new ApplicationConfiguration
        {
            AdminUsers = [new AdminUserConfiguration { Name = "Admin", Password = "pass1" }],
            Languages = ["csharp", "sql"],
            MaxUsersPerRoom = 3
        });

        return new ConfigurationLanguageValidator(settings);
    }
}

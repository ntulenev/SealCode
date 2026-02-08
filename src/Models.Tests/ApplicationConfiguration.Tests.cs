using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Models.Configuration;

namespace Models.Tests;

public sealed class ApplicationConfigurationTests
{
    [Fact(DisplayName = "ValidationShouldPassWhenConfigurationIsValid")]
    [Trait("Category", "Unit")]
    public void ValidationShouldPassWhenConfigurationIsValid()
    {
        var config = new ApplicationConfiguration
        {
            AdminUsers = [new AdminUserConfiguration { Name = "Admin", Password = "pass1" }],
            Languages = ["csharp", "sql"],
            MaxUsersPerRoom = 3
        };

        var isValid = Validator.TryValidateObject(config, new ValidationContext(config), [], true);

        isValid.Should().BeTrue();
    }

    [Fact(DisplayName = "ValidationShouldFailWhenAdminUsersIsNull")]
    [Trait("Category", "Unit")]
    public void ValidationShouldFailWhenAdminUsersIsNull()
    {
        var config = new ApplicationConfiguration
        {
            AdminUsers = null!,
            Languages = ["csharp"],
            MaxUsersPerRoom = 3
        };

        var isValid = Validator.TryValidateObject(config, new ValidationContext(config), [], true);

        isValid.Should().BeFalse();
    }

    [Fact(DisplayName = "ValidationShouldFailWhenAdminUsersIsEmpty")]
    [Trait("Category", "Unit")]
    public void ValidationShouldFailWhenAdminUsersIsEmpty()
    {
        var config = new ApplicationConfiguration
        {
            AdminUsers = [],
            Languages = ["csharp"],
            MaxUsersPerRoom = 3
        };

        var isValid = Validator.TryValidateObject(config, new ValidationContext(config), [], true);

        isValid.Should().BeFalse();
    }

    [Theory(DisplayName = "ValidationShouldFailWhenMaxUsersPerRoomIsOutOfRange")]
    [Trait("Category", "Unit")]
    [InlineData(0)]
    [InlineData(6)]
    public void ValidationShouldFailWhenMaxUsersPerRoomIsOutOfRange(int value)
    {
        var config = new ApplicationConfiguration
        {
            AdminUsers = [new AdminUserConfiguration { Name = "Admin", Password = "pass1" }],
            Languages = ["csharp"],
            MaxUsersPerRoom = value
        };

        var isValid = Validator.TryValidateObject(config, new ValidationContext(config), [], true);

        isValid.Should().BeFalse();
    }

    [Fact(DisplayName = "ValidationShouldFailWhenLanguagesIsNull")]
    [Trait("Category", "Unit")]
    public void ValidationShouldFailWhenLanguagesIsNull()
    {
        var config = new ApplicationConfiguration
        {
            AdminUsers = [new AdminUserConfiguration { Name = "Admin", Password = "pass1" }],
            Languages = null!,
            MaxUsersPerRoom = 3
        };

        var isValid = Validator.TryValidateObject(config, new ValidationContext(config), [], true);

        isValid.Should().BeFalse();
    }

    [Fact(DisplayName = "ValidationShouldFailWhenLanguagesIsEmpty")]
    [Trait("Category", "Unit")]
    public void ValidationShouldFailWhenLanguagesIsEmpty()
    {
        var config = new ApplicationConfiguration
        {
            AdminUsers = [new AdminUserConfiguration { Name = "Admin", Password = "pass1" }],
            Languages = [],
            MaxUsersPerRoom = 3
        };

        var isValid = Validator.TryValidateObject(config, new ValidationContext(config), [], true);

        isValid.Should().BeFalse();
    }
}

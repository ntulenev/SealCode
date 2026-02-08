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
            MaxUsersPerRoom = value
        };

        var isValid = Validator.TryValidateObject(config, new ValidationContext(config), [], true);

        isValid.Should().BeFalse();
    }
}

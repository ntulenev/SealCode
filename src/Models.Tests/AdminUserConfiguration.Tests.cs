using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Models.Configuration;

namespace Models.Tests;

public sealed class AdminUserConfigurationTests
{
    [Fact(DisplayName = "ValidationShouldPassWhenAdminUserIsValid")]
    [Trait("Category", "Unit")]
    public void ValidationShouldPassWhenAdminUserIsValid()
    {
        var user = new AdminUserConfiguration { Name = "Admin", Password = "pass1" };

        var isValid = Validator.TryValidateObject(user, new ValidationContext(user), [], true);

        isValid.Should().BeTrue();
    }

    [Theory(DisplayName = "ValidationShouldFailWhenNameIsInvalid")]
    [Trait("Category", "Unit")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A")]
    public void ValidationShouldFailWhenNameIsInvalid(string? value)
    {
        var user = new AdminUserConfiguration { Name = value!, Password = "pass1" };

        var isValid = Validator.TryValidateObject(user, new ValidationContext(user), [], true);

        isValid.Should().BeFalse();
    }

    [Theory(DisplayName = "ValidationShouldFailWhenPasswordIsInvalid")]
    [Trait("Category", "Unit")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void ValidationShouldFailWhenPasswordIsInvalid(string? value)
    {
        var user = new AdminUserConfiguration { Name = "Admin", Password = value! };

        var isValid = Validator.TryValidateObject(user, new ValidationContext(user), [], true);

        isValid.Should().BeFalse();
    }
}

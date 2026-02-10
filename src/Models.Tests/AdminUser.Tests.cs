using FluentAssertions;

namespace Models.Tests;

public sealed class AdminUserTests
{
    [Fact(DisplayName = "CtorShouldTrimValue")]
    [Trait("Category", "Unit")]
    public void CtorShouldTrimValue()
    {
        var user = new AdminUser(" admin ");

        user.Name.Should().Be("admin");
    }

    [Theory(DisplayName = "CtorShouldThrowWhenValueIsWhiteSpace")]
    [Trait("Category", "Unit")]
    [InlineData("")]
    [InlineData(" ")]
    public void CtorShouldThrowWhenValueIsWhiteSpace(string value)
    {
        var action = () => new AdminUser(value);

        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "CtorShouldThrowWhenValueIsNull")]
    [Trait("Category", "Unit")]
    public void CtorShouldThrowWhenValueIsNull()
    {
        var action = () => new AdminUser(null!);

        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "CtorShouldSetSuperAdminFlag")]
    [Trait("Category", "Unit")]
    public void CtorShouldSetSuperAdminFlag()
    {
        var user = new AdminUser("Admin", true);

        user.IsSuperAdmin.Should().BeTrue();
    }

    [Fact(DisplayName = "MatchesShouldIgnoreCase")]
    [Trait("Category", "Unit")]
    public void MatchesShouldIgnoreCase()
    {
        var owner = new AdminUser("Admin");
        var other = new AdminUser("admin");

        owner.Matches(other).Should().BeTrue();
    }
}

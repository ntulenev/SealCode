using Microsoft.Extensions.Options;

using Models.Configuration;

public sealed class ApplicationConfigurationValidator : IValidateOptions<ApplicationConfiguration>
{
    public ValidateOptionsResult Validate(string? name, ApplicationConfiguration options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            errors.Add("AdminPassword is required.");
        }
        else if (string.Equals(options.AdminPassword, "change-me", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("AdminPassword must be changed from the default value.");
        }

        if (options.MaxUsersPerRoom is < 1 or > 5)
        {
            errors.Add("MaxUsersPerRoom must be between 1 and 5.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}

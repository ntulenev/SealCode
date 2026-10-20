using System.ComponentModel.DataAnnotations;
using System.Buffers.Binary;

using Microsoft.Extensions.Options;

using Models.Configuration;

namespace SealCode;

internal sealed class ApplicationConfigurationValidator(IWebHostEnvironment environment) : IValidateOptions<ApplicationConfiguration>
{
    public ValidateOptionsResult Validate(string? name, ApplicationConfiguration options)
    {
        var errors = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var admin in options.AdminUsers ?? [])
        {
            if (admin is null)
            { errors.Add("Admin record cannot be null."); continue; }
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(admin, new(admin), results, true))
                errors.Add("Invalid admin record.");
            if (string.IsNullOrWhiteSpace(admin.Name) || admin.Name.Trim().Length is < 2 or > 64 || !names.Add(admin.Name.Trim()))
                errors.Add("Admin names must be unique and contain 2–64 characters.");
            if (!environment.IsDevelopment() && (string.IsNullOrWhiteSpace(admin.PasswordHash) || !string.IsNullOrEmpty(admin.Password)))
                errors.Add("Production requires PasswordHash; remove legacy Password from configuration.");
            if (admin.PasswordHash is not null)
            {
                try
                {
                    var bytes = Convert.FromBase64String(admin.PasswordHash);
                    // Identity V3: marker + PRF + iterations + salt size + salt + subkey.
                    if (bytes.Length < 61 || bytes[0] != 1
                        || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1)) > 2
                        || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5)) is < 10000 or > 1000000
                        || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(9)) is < 16 or > 64
                        || 13L + BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(9)) + 16 > bytes.Length)
                        errors.Add("Invalid Identity V3 password hash.");
                }
                catch (FormatException) { errors.Add("Invalid password hash encoding."); }
            }
        }
        if (options.Languages is null || !options.Languages.Any(language => !string.IsNullOrWhiteSpace(language)))
            errors.Add("At least one effective language is required.");
        var limitErrors = new List<ValidationResult>();
        if (options.Limits is null || !Validator.TryValidateObject(options.Limits, new(options.Limits), limitErrors, true))
            errors.Add("Invalid room limits.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

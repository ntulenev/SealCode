using System.Buffers.Binary;
using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Options;

using Models.Configuration;

namespace SealCode;

/// <summary>
/// Validates nested application settings and environment-specific security requirements.
/// </summary>
internal sealed class ApplicationConfigurationValidator : IValidateOptions<ApplicationConfiguration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationConfigurationValidator"/> class.
    /// </summary>
    /// <param name="environment">The hosting environment.</param>
    /// <exception cref="ArgumentNullException">Thrown when the environment is null.</exception>
    public ApplicationConfigurationValidator(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        _environment = environment;
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ApplicationConfiguration options)
    {
        var errors = new List<string>();
        foreach (var proxy in options.TrustedProxies)
        {
            if (!System.Net.IPAddress.TryParse(proxy, out _))
            {
                errors.Add($"TrustedProxies contains an invalid IP address: {proxy}");
            }
        }
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var admin in options.AdminUsers ?? [])
        {
            if (admin is null)
            {
                errors.Add("Admin record cannot be null.");
                continue;
            }
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(admin, new(admin), results, true))
            {
                errors.Add("Invalid admin record.");
            }

            if (string.IsNullOrWhiteSpace(admin.Name)
                || admin.Name.Trim().Length is < 2 or > 64
                || !names.Add(admin.Name.Trim()))
            {
                errors.Add("Admin names must be unique and contain 2–64 characters.");
            }

            if (!string.IsNullOrEmpty(admin.McpToken)
                && (admin.McpToken.Length is < 32 or > 1024
                    || !IsBearerToken(admin.McpToken)
                    || !tokens.Add(admin.McpToken)))
            {
                errors.Add("MCP tokens must be unique, contain 32–1024 characters and use the Bearer token alphabet.");
            }

            if (!_environment.IsDevelopment()
                && (string.IsNullOrWhiteSpace(admin.PasswordHash)
                    || !string.IsNullOrEmpty(admin.Password)))
            {
                errors.Add("Production requires PasswordHash; remove legacy Password from configuration.");
            }

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
                    {
                        errors.Add("Invalid Identity V3 password hash.");
                    }
                }
                catch (FormatException) { errors.Add("Invalid password hash encoding."); }
            }
        }
        if (options.Languages is null || !options.Languages.Any(language => !string.IsNullOrWhiteSpace(language)))
        {
            errors.Add("At least one effective language is required.");
        }

        var limitErrors = new List<ValidationResult>();
        if (options.Limits is null
            || !Validator.TryValidateObject(
                options.Limits,
                new(options.Limits),
                limitErrors,
                true))
        {
            errors.Add("Invalid room limits.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsBearerToken(string token)
    {
        var body = token.TrimEnd('=');
        return body.Length > 0 && body.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~' or '+' or '/');
    }

    private readonly IWebHostEnvironment _environment;
}

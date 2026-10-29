using Abstractions;

using Logic;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

using Models;
using Models.Configuration;

using SealCode.Routing;
using SealCode.Security;

using Transport.Serialization;

namespace SealCode.Hosting;

/// <summary>
/// Registers the application services by responsibility.
/// </summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers configuration services.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">The application configuration source.</param>
    /// <returns>The service collection for further registration.</returns>
    public static IServiceCollection AddSealCodeConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        _ = services.AddOptions<ApplicationConfiguration>()
            .Bind(configuration)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = services.AddSingleton<IValidateOptions<ApplicationConfiguration>, ApplicationConfigurationValidator>();
        _ = services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
            options.SerializerOptions.Converters.Add(new ShortGuidJsonConverter());
        });
        _ = services.Configure<RouteOptions>(options => options.ConstraintMap["ShortGuid"] = typeof(ShortGuidRouteConstraint));
        return services;
    }

    /// <summary>
    /// Registers security services.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="environment">The hosting environment used to select security defaults.</param>
    /// <returns>The service collection for further registration.</returns>
    public static IServiceCollection AddSealCodeSecurity(this IServiceCollection services, IWebHostEnvironment environment)
    {
        _ = services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        _ = services.AddSingleton<AdminHttpSecurity>();
        _ = services.AddHttpContextAccessor();
        _ = services.AddScoped<IAdminUserManager, AdminUserManager>();
        _ = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, McpTokenAuthenticationHandler>(
                McpTokenAuthenticationHandler.SCHEME_NAME, _ => { })
            .AddCookie(options =>
            {
                options.Cookie.Name = "admin_auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                options.SlidingExpiration = false;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
            });

        _ = services.AddAuthorization(options => options.AddPolicy(
            McpTokenAuthenticationHandler.SCHEME_NAME,
            policy => policy.AddAuthenticationSchemes(McpTokenAuthenticationHandler.SCHEME_NAME)
                .RequireAuthenticatedUser()));

        _ = services.AddSingleton<IConfigureOptions<ForwardedHeadersOptions>, TrustedProxyOptions>();
        return services;
    }

    /// <summary>
    /// Registers room services.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The service collection for further registration.</returns>
    public static IServiceCollection AddSealCodeRooms(this IServiceCollection services)
    {
        _ = services.AddSingleton(TimeProvider.System);
        _ = services.AddHostedService<IdleRoomCleanupService>();
        _ = services.AddSingleton<IRoomRegistry, RoomRegistry>();
        _ = services.AddSingleton<IRoomNotifier, SignalRRoomNotifier>();
        _ = services.AddSingleton<IRoomManager, RoomManager>();
        _ = services.AddSingleton<IRoomSessionService, RoomSessionService>();
        _ = services.AddSingleton<ILanguageValidator, ConfigurationLanguageValidator>();
        return services;
    }

    /// <summary>
    /// Registers SignalR services.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The service collection for further registration.</returns>
    public static IServiceCollection AddSealCodeSignalR(this IServiceCollection services)
    {
        _ = services.AddSingleton<RoomResourceFilter>();
        _ = services.AddSignalR(options =>
            {
                options.AddFilter<RoomResourceFilter>();
                options.MaximumReceiveMessageSize = RoomLimits.MaxSignalRMessageBytes;
                options.KeepAliveInterval = TimeSpan.FromSeconds(5);
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(15);
            })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = null;
                options.PayloadSerializerOptions.Converters.Add(new ShortGuidJsonConverter());
            });
        return services;
    }

}

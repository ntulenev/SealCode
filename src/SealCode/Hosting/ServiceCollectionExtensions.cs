using Abstractions;

using Logic;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using ModelContextProtocol.AspNetCore;

using Models;
using Models.Configuration;

using Npgsql;

using Persistence.Snippets;
using Persistence.Statistics;

using SealCode.Mcp;
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
        _ = services.AddSingleton<IGoogleIdentityVerifier, GoogleIdentityVerifier>();
        _ = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, McpTokenAuthenticationHandler>(
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

    /// <summary>Registers one lazy, host-owned PostgreSQL pool for snippets and statistics.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The service collection for further registration.</returns>
    private static IServiceCollection AddSealCodeDatabase(this IServiceCollection services)
    {
        services.TryAddSingleton(provider =>
        {
            var connection = provider.GetRequiredService<IConfiguration>().GetConnectionString("SealCode");
            if (string.IsNullOrWhiteSpace(connection))
            {
                throw new InvalidOperationException("Set ConnectionStrings:SealCode and apply the database migrations.");
            }
            return NpgsqlDataSource.Create(connection);
        });
        return services;
    }

    /// <summary>Registers statistics using the shared application pool and the tracked room creator.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The service collection for further registration.</returns>
    public static IServiceCollection AddSealCodeStatistics(this IServiceCollection services)
    {
        _ = services.AddSealCodeDatabase();
        _ = services.AddSingleton<TrackedRoomCreator>();
        _ = services.AddSingleton<IUsageStatisticsRepository>(provider =>
            string.IsNullOrWhiteSpace(provider.GetRequiredService<IConfiguration>().GetConnectionString("SealCode"))
                ? new DisabledUsageStatisticsRepository()
                : new PostgreSqlUsageStatisticsRepository(() => provider.GetRequiredService<NpgsqlDataSource>()));
        return services;
    }

    /// <summary>
    /// Registers the stateless admin MCP transport and its explicit tool collection.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The service collection for further registration.</returns>
    public static IServiceCollection AddSealCodeMcp(this IServiceCollection services)
    {
        _ = services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithTools<AdminMcpTools>()
            .WithTools<StatisticsMcpTools>()
            .WithTools<SnippetMcpTools>();
        return services;
    }

    /// <summary>
    /// Registers the selected repository, application catalog and startup/refresh workers.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="environment">The host containing private library resources.</param>
    /// <returns>The service collection for further registration.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
    public static IServiceCollection AddSealCodeSnippets(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        _ = services.AddSealCodeDatabase();
        _ = services.AddSingleton<ISnippetRepository>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<ApplicationConfiguration>>().Value.Snippets;
            if (settings.Enabled && settings.Provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
            {
                return new PostgreSqlSnippetRepository(() => provider.GetRequiredService<NpgsqlDataSource>());
            }
            return new FileSnippetRepository(Path.Combine(environment.ContentRootPath, "Resources", "Snippets"));
        });
        _ = services.AddSingleton<ISnippetCatalog, SnippetCatalog>();
        _ = services.AddSingleton<SnippetAdministration>();
        _ = services.AddHostedService<SnippetCatalogStartupService>();
        _ = services.AddHostedService<SnippetCatalogRefreshService>();
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
        _ = services.AddSingleton<SealCode.Observers.ObserverSessions>();
        _ = services.AddHostedService(provider => provider.GetRequiredService<SealCode.Observers.ObserverSessions>());
        _ = services.AddSignalR(options =>
            {
                options.MaximumReceiveMessageSize = RoomLimits.MaxSignalRMessageBytes;
                options.KeepAliveInterval = TimeSpan.FromSeconds(5);
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(15);
            })
            .AddHubOptions<RoomHub>(options => options.AddFilter<RoomResourceFilter>())
            .AddHubOptions<SealCode.Observers.ObserverHub>(options =>
            {
                options.MaximumReceiveMessageSize = 8192;
                options.AddFilter<SealCode.Observers.ObserverFilter>();
            })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = null;
                options.PayloadSerializerOptions.Converters.Add(new ShortGuidJsonConverter());
            });
        return services;
    }

}

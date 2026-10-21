using System.Net;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

using Models.Configuration;

namespace SealCode.Hosting;

/// <summary>
/// Configures forwarded headers from validated application settings.
/// </summary>
internal sealed class TrustedProxyOptions : IConfigureOptions<ForwardedHeadersOptions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrustedProxyOptions"/> class.
    /// </summary>
    /// <param name="configuration">The validated application settings.</param>
    public TrustedProxyOptions(IOptions<ApplicationConfiguration> configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public void Configure(ForwardedHeadersOptions options)
    {
        var proxies = _configuration.Value.TrustedProxies;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        if (proxies.Length > 0)
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in proxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        }
    }

    private readonly IOptions<ApplicationConfiguration> _configuration;
}

using System.Net;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

using Models.Configuration;

namespace SealCode.Hosting;

/// <summary>
/// Configures forwarded headers from validated application settings.
/// </summary>
internal sealed class TrustedProxyOptions(IOptions<ApplicationConfiguration> configuration) : IConfigureOptions<ForwardedHeadersOptions>
{
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

    private readonly IOptions<ApplicationConfiguration> _configuration = configuration;
}

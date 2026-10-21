using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

using Models.Configuration;
using Models.Exceptions;

using SealCode.Hub;

namespace SealCode;

/// <summary>
/// Enforces hub resource budgets and translates domain failures to protocol errors.
/// </summary>
internal sealed class RoomResourceFilter : IHubFilter, IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RoomResourceFilter"/> class.
    /// </summary>
    /// <param name="settings">The application settings.</param>
    /// <exception cref="ArgumentNullException">Thrown when the settings are null.</exception>
    public RoomResourceFilter(IOptions<ApplicationConfiguration> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _limits = settings.Value.Limits;
        _sources = [.. Enumerable.Range(0, 256).Select(_ => Limiter(_limits.NewConnectionsPerSourcePerMinute, 60))];
        _connections = new SemaphoreSlim(_limits.MaxConnections);
    }

    /// <inheritdoc />
    public async Task OnConnectedAsync(
        HubLifetimeContext context,
        Func<HubLifetimeContext, Task> next)
    {
        var address = context.Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        using var lease = _sources[(uint)StringComparer.Ordinal.GetHashCode(address) % 256].AttemptAcquire();
        if (!lease.IsAcquired || !_connections.Wait(0))
        {
            _throttled.Add(1);
            context.Context.Abort();
            throw HubErrors.Create(HubErrorCode.RateLimited, "Connection capacity reached");
        }
        context.Context.Items["invocationLimiter"] = Limiter(_limits.InvocationsPerSecond, 1);
        context.Context.Items["connectionPermit"] = true;
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch { Release(context); throw; }
    }

    /// <inheritdoc />
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        try
        {
            await next(context, exception).ConfigureAwait(false);
        }
        finally { Release(context); }
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (context.Context.Items.TryGetValue("invocationLimiter", out var value))
        {
            using var lease = ((FixedWindowRateLimiter)value!).AttemptAcquire();
            if (!lease.IsAcquired)
            {
                _throttled.Add(1);
                throw HubErrors.Create(HubErrorCode.RateLimited, "Try again later");
            }
        }
        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException) { throw HubErrors.Create(HubErrorCode.NotParticipant, ""); }
        catch (ArgumentException) { throw HubErrors.Create(HubErrorCode.InvalidInput, "Invalid room input"); }
        catch (RoomNotFoundException) { throw HubErrors.Create(HubErrorCode.RoomNotFound, ""); }
        catch (RoomLimitException ex) { throw HubErrors.Create(HubErrorCode.LimitExceeded, ex.Message["LimitExceeded: ".Length..]); }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _connections.Dispose();
        foreach (var source in _sources)
        {
            source.Dispose();
        }
    }

    private static FixedWindowRateLimiter Limiter(int permits, int seconds) => new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = permits,
        Window = TimeSpan.FromSeconds(seconds),
        QueueLimit = 0,
        AutoReplenishment = true
    });

    private void Release(HubLifetimeContext context)
    {
        if (context.Context.Items.Remove("connectionPermit"))
        {
            _ = _connections.Release();
        }

        if (context.Context.Items.Remove("invocationLimiter", out var limiter))
        {
            ((IDisposable)limiter!).Dispose();
        }
    }

    private readonly SemaphoreSlim _connections;
    private readonly RoomLimits _limits;
    // Fixed buckets bound anonymous-source limiter memory even under rotating addresses.
    private readonly FixedWindowRateLimiter[] _sources;
    private static readonly Meter _meter = new("SealCode.Hub");
    private static readonly Counter<long> _throttled = _meter.CreateCounter<long>("hub.throttled");
}

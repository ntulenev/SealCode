using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

using Models.Configuration;
using Models.Exceptions;

namespace SealCode;

internal sealed class RoomResourceFilter(IOptions<ApplicationConfiguration> settings) : IHubFilter, IDisposable
{
    private readonly SemaphoreSlim _connections = new(settings.Value.Limits.MaxConnections);
    // Fixed buckets bound anonymous-source limiter memory even under rotating addresses.
    private readonly FixedWindowRateLimiter[] _sources = [.. Enumerable.Range(0, 256).Select(_ => Limiter(30, 60))];
    private static readonly Meter _meter = new("SealCode.Hub");
    private static readonly Counter<long> _throttled = _meter.CreateCounter<long>("hub.throttled");

    private static FixedWindowRateLimiter Limiter(int permits, int seconds) => new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = permits,
        Window = TimeSpan.FromSeconds(seconds),
        QueueLimit = 0,
        AutoReplenishment = true
    });

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var address = context.Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        using var lease = _sources[(uint)StringComparer.Ordinal.GetHashCode(address) % 256].AttemptAcquire();
        if (!lease.IsAcquired || !_connections.Wait(0))
        {
            _throttled.Add(1);
            context.Context.Abort();
            throw new HubException("RateLimited: Connection capacity reached");
        }
        context.Context.Items["invocationLimiter"] = Limiter(settings.Value.Limits.InvocationsPerSecond, 1);
        context.Context.Items["connectionPermit"] = true;
        try
        { await next(context).ConfigureAwait(false); }
        catch { Release(context); throw; }
    }

    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        try
        { await next(context, exception).ConfigureAwait(false); }
        finally { Release(context); }
    }

    private void Release(HubLifetimeContext context)
    {
        if (context.Context.Items.Remove("connectionPermit"))
            _ = _connections.Release();
        if (context.Context.Items.Remove("invocationLimiter", out var limiter))
            ((IDisposable)limiter!).Dispose();
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (context.Context.Items.TryGetValue("invocationLimiter", out var value))
        {
            using var lease = ((FixedWindowRateLimiter)value!).AttemptAcquire();
            if (!lease.IsAcquired)
            {
                _throttled.Add(1);
                throw new HubException("RateLimited: Try again later");
            }
        }
        try
        { return await next(context).ConfigureAwait(false); }
        catch (UnauthorizedAccessException ex) { throw new HubException(ex.Message); }
        catch (ArgumentException) { throw new HubException("InvalidInput: Invalid room input"); }
        catch (RoomNotFoundException ex) { throw new HubException(ex.Message); }
        catch (RoomLimitException ex) { throw new HubException(ex.Message); }
    }

    public void Dispose()
    {
        _connections.Dispose();
        foreach (var source in _sources)
            source.Dispose();
    }
}

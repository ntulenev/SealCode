using System.Diagnostics.Metrics;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

using Models.Configuration;
using Models.Exceptions;

using SealCode.Hub;
using SealCode.Hosting;

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
    /// <param name="timeProvider">The connection deadline clock.</param>
    /// <exception cref="ArgumentNullException">Thrown when the settings are null.</exception>
    public RoomResourceFilter(IOptions<ApplicationConfiguration> settings, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _timeProvider = timeProvider ?? TimeProvider.System;
        _limits = settings.Value.Limits;
        _sources = [.. Enumerable.Range(0, 256).Select(_ => Limiter(_limits.NewConnectionsPerSourcePerMinute, 60))];
        _connections = new SemaphoreSlim(_limits.MaxConnections);
    }

    /// <inheritdoc />
    public async Task OnConnectedAsync(
        HubLifetimeContext context,
        Func<HubLifetimeContext, Task> next)
    {
        var address = ConnectionSource.From(context.Context.GetHttpContext()?.Connection.RemoteIpAddress);
        using var lease = _sources[(uint)StringComparer.Ordinal.GetHashCode(address) % 256].AttemptAcquire();
        if (!lease.IsAcquired || !_connections.Wait(0))
        {
            _throttled.Add(1);
            context.Context.Abort();
            throw HubErrors.Create(HubErrorCode.RateLimited, "Connection capacity reached");
        }
        lock (_sourceLock)
        {
            var count = _activeSources.GetValueOrDefault(address);
            if (count >= _limits.MaxConnectionsPerSource)
            {
                _ = _connections.Release();
                _throttled.Add(1);
                context.Context.Abort();
                throw HubErrors.Create(HubErrorCode.RateLimited, "Connection capacity reached");
            }
            _activeSources[address] = count + 1;
        }
        context.Context.Items["connectionSource"] = address;
        context.Context.Items["unjoinedTimer"] = _timeProvider.CreateTimer(_ =>
        {
            if (context.Context.GetActiveRoomId() is null)
            {
                context.Context.Abort();
            }
        }, null, TimeSpan.FromSeconds(_limits.UnjoinedConnectionTimeoutSeconds), Timeout.InfiniteTimeSpan);
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
        if (context.Context.Items.Remove("unjoinedTimer", out var timer))
        {
            ((ITimer)timer!).Dispose();
        }
        if (context.Context.Items.Remove("connectionSource", out var source))
        {
            lock (_sourceLock)
            {
                var key = (string)source!;
                if (--_activeSources[key] == 0)
                {
                    _ = _activeSources.Remove(key);
                }
            }
        }
        if (context.Context.Items.Remove("connectionPermit"))
        {
            _ = _connections.Release();
        }

        if (context.Context.Items.Remove("invocationLimiter", out var limiter))
        {
            ((IDisposable)limiter!).Dispose();
        }
    }

    internal int TrackedSourceCount
    {
        get
        {
            lock (_sourceLock)
            {
                return _activeSources.Count;
            }
        }
    }

    private readonly Lock _sourceLock = new();
    private readonly Dictionary<string, int> _activeSources = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _connections;
    private readonly RoomLimits _limits;
    // Fixed buckets bound anonymous-source limiter memory even under rotating addresses.
    private readonly FixedWindowRateLimiter[] _sources;
    private static readonly Meter _meter = new("SealCode.Hub");
    private static readonly Counter<long> _throttled = _meter.CreateCounter<long>("hub.throttled");
}

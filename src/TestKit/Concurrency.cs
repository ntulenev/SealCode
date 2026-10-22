using System.Collections.Concurrent;

namespace TestKit;

internal static class Concurrency
{
    internal static async Task RunTogether(IReadOnlyList<Action> actions)
    {
        if (actions.Count == 0)
        {
            return;
        }
        using var barrier = new Barrier(actions.Count);
        var failures = new ConcurrentQueue<Exception>();
        var tasks = actions.Select(action => Task.Factory.StartNew(() =>
        {
            barrier.SignalAndWait();
            try
            {
                action();
            }
            catch (Exception error)
            {
                failures.Enqueue(error);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        if (!failures.IsEmpty)
        {
            throw new AggregateException(failures);
        }
    }
}

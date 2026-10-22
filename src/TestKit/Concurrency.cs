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
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
            {
                failures.Enqueue(new TimeoutException("Concurrent start timed out"));
                return;
            }
            try
            {
                action();
            }
            catch (Exception error)
            {
                failures.Enqueue(error);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (!failures.IsEmpty)
        {
            throw new AggregateException(failures);
        }
    }
}

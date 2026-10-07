namespace YoutubePlaylistDownloader.Utilities;

internal static class DownloadGate
{
    private sealed class Waiter
    {
        public string JobId;
        public TaskCompletionSource<bool> Tcs;
        public CancellationToken Token;
    }

    private static readonly object Sync = new();
    private static readonly List<Waiter> Waiters = [];
    private static int running;
    private static int limit = 2;

    public static Func<string, int> PriorityOf { get; set; }

    public static int Limit
    {
        get
        {
            lock (Sync)
                return limit;
        }
        set
        {
            lock (Sync)
            {
                limit = Math.Clamp(value, 1, 16);
                StartWaiters();
            }
        }
    }

    public static Task WaitAsync(string jobId, CancellationToken token)
    {
        TaskCompletionSource<bool> tcs;
        lock (Sync)
        {
            if (running < limit)
            {
                running++;
                return Task.CompletedTask;
            }

            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Waiters.Add(new Waiter { JobId = jobId ?? "", Tcs = tcs, Token = token });
        }

        if (token.CanBeCanceled)
        {
            token.Register(() =>
            {
                lock (Sync)
                    Waiters.RemoveAll(x => x.Tcs == tcs);
                tcs.TrySetCanceled(token);
            });
        }

        return tcs.Task;
    }

    public static void Release()
    {
        lock (Sync)
        {
            running = Math.Max(0, running - 1);
            StartWaiters();
        }
    }

    public static void NotifyQueueChanged()
    {
        lock (Sync)
            StartWaiters();
    }

    private static void StartWaiters()
    {
        while (running < limit && Waiters.Count > 0)
        {
            Waiter best = null;
            var bestPriority = int.MaxValue;
            foreach (var waiter in Waiters)
            {
                var priority = PriorityOf?.Invoke(waiter.JobId) ?? int.MaxValue;
                if (best == null || priority < bestPriority)
                {
                    best = waiter;
                    bestPriority = priority;
                }
            }

            if (best == null)
                return;

            Waiters.Remove(best);
            if (best.Token.IsCancellationRequested)
            {
                best.Tcs.TrySetCanceled(best.Token);
                continue;
            }

            running++;
            best.Tcs.TrySetResult(true);
        }
    }
}

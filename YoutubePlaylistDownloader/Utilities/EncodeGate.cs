namespace YoutubePlaylistDownloader.Utilities;

internal static class EncodeGate
{
    private sealed class Waiter
    {
        public TaskCompletionSource<bool> Tcs;
        public CancellationToken Token;
    }

    private static readonly object Sync = new();
    private static readonly List<Waiter> Waiters = [];
    private static int running;
    private static int limit = 2;

    public static event Action LimitChanged;
    public static event Action<int, object> UiChanged;

    public static void SetLimit(int value, object source)
    {
        var limit = DownloadSettings.NormalizeSimultaneousDownloads(value);
        var store = GlobalConsts.DownloadSettings.MaxSimultaneousEncodes != limit;
        GlobalConsts.DownloadSettings.MaxSimultaneousEncodes = limit;
        Limit = limit;
        if (store)
            GlobalConsts.NySaveDownloadSettings();
        UiChanged?.Invoke(limit, source);
    }

    public static int Limit
    {
        get
        {
            lock (Sync)
                return limit;
        }
        set
        {
            var changed = false;
            lock (Sync)
            {
                var next = Math.Clamp(value, 1, 16);
                if (next != limit)
                {
                    limit = next;
                    changed = true;
                }

                StartWaiters();
            }

            if (changed)
                LimitChanged?.Invoke();
        }
    }

    public static Task WaitAsync(CancellationToken token) => WaitCore(token, front: false);

    // The caller still holds its slot. Release() afterwards starts this waiter
    // before any file that has not run yet.
    public static Task WaitFrontAsync(CancellationToken token) => WaitCore(token, front: true, holdingSlot: true);

    static Task WaitCore(CancellationToken token, bool front, bool holdingSlot = false)
    {
        TaskCompletionSource<bool> tcs;
        lock (Sync)
        {
            if (!holdingSlot && running < limit)
            {
                running++;
                return Task.CompletedTask;
            }

            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiter = new Waiter { Tcs = tcs, Token = token };
            if (front)
                Waiters.Insert(0, waiter);
            else
                Waiters.Add(waiter);
        }

        if (token.CanBeCanceled)
        {
            token.Register(() =>
            {
                lock (Sync)
                    Waiters.RemoveAll(waiter => waiter.Tcs == tcs);
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

    private static void StartWaiters()
    {
        while (running < limit && Waiters.Count > 0)
        {
            var waiter = Waiters[0];
            Waiters.RemoveAt(0);
            if (waiter.Token.IsCancellationRequested)
            {
                waiter.Tcs.TrySetCanceled(waiter.Token);
                continue;
            }

            running++;
            waiter.Tcs.TrySetResult(true);
        }
    }
}

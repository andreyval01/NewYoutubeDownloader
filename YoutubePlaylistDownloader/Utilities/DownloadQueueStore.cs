namespace YoutubePlaylistDownloader.Utilities;

internal sealed class PersistedDownloadJob
{
    public string Id { get; set; }
    public string Url { get; set; }
    public string Title { get; set; }
    public string SavePath { get; set; }
    public string ImageUrl { get; set; }
    public DownloadSettings Settings { get; set; }
    public int DownloadedCount { get; set; }
    public int TotalCount { get; set; }
    public int NextIndex { get; set; }
    public string CurrentTitle { get; set; }
    public string CurrentStatus { get; set; }
    public int CurrentProgressPercent { get; set; }
    public bool StillDownloading { get; set; }
    public bool Paused { get; set; }
    public int SortOrder { get; set; }
    public List<string> PausedVideoIds { get; set; } = [];
}

internal sealed class PersistedPendingLink
{
    public string Url { get; set; }
    public string Title { get; set; }
    public DownloadSettings Settings { get; set; }
}

internal sealed class DownloadQueueSnapshot
{
    public List<PersistedDownloadJob> Jobs { get; set; } = [];
    public List<PersistedPendingLink> Pending { get; set; } = [];
}

internal static class DownloadQueueStore
{
    private static readonly object Sync = new();
    private static readonly string FilePath = Path.Combine(AppPaths.AppDataDirectory, "DownloadQueue.json");

    private static List<PersistedDownloadJob> jobs = [];
    private static List<PersistedPendingLink> pending = [];
    private static bool loaded;
    private static bool dirty;
    private static CancellationTokenSource debounceCts;

    public static DownloadQueueSnapshot GetSnapshot()
    {
        lock (Sync)
        {
            EnsureLoaded();
            return new DownloadQueueSnapshot
            {
                Jobs = jobs.Select(CloneJob).ToList(),
                Pending = pending.Select(ClonePending).ToList()
            };
        }
    }

    public static void UpsertJob(PersistedDownloadJob job, bool flushNow)
    {
        if (job == null || string.IsNullOrWhiteSpace(job.Id))
            return;

        lock (Sync)
        {
            EnsureLoaded();
            var index = jobs.FindIndex(x => string.Equals(x.Id, job.Id, StringComparison.OrdinalIgnoreCase));
            var copy = CloneJob(job);
            if (index >= 0)
                jobs[index] = copy;
            else
                jobs.Add(copy);
            dirty = true;
            if (flushNow)
                WriteUnlocked();
            else
                ScheduleUnlocked();
        }
    }

    public static void SetOrder(IReadOnlyList<string> ids)
    {
        if (ids == null)
            return;

        lock (Sync)
        {
            EnsureLoaded();
            var map = jobs.ToDictionary(x => x.Id ?? "", StringComparer.OrdinalIgnoreCase);
            var ordered = new List<PersistedDownloadJob>();
            var sort = 0;
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id) || !map.Remove(id, out var job))
                    continue;
                job.SortOrder = sort++;
                ordered.Add(job);
            }

            foreach (var job in map.Values)
            {
                job.SortOrder = sort++;
                ordered.Add(job);
            }

            jobs = ordered;
            dirty = true;
            WriteUnlocked();
        }
    }

    public static void RemoveJob(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        lock (Sync)
        {
            EnsureLoaded();
            var removed = jobs.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (removed <= 0)
                return;
            dirty = true;
            WriteUnlocked();
        }
    }

    public static void AddPending(string url, string title, DownloadSettings settings)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        lock (Sync)
        {
            EnsureLoaded();
            pending.RemoveAll(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase));
            pending.Add(new PersistedPendingLink
            {
                Url = url,
                Title = title ?? "",
                Settings = settings?.NyClone()
            });
            dirty = true;
            WriteUnlocked();
        }
    }

    public static void RemovePending(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        lock (Sync)
        {
            EnsureLoaded();
            var removed = pending.RemoveAll(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase));
            if (removed <= 0)
                return;
            dirty = true;
            WriteUnlocked();
        }
    }

    public static void NyFlush()
    {
        lock (Sync)
        {
            EnsureLoaded();
            if (dirty)
                WriteUnlocked();
        }
    }

    private static void ScheduleUnlocked()
    {
        debounceCts?.Cancel();
        debounceCts = new CancellationTokenSource();
        var token = debounceCts.Token;
        _ = Task.Delay(1500, token).ContinueWith(_ =>
        {
            if (token.IsCancellationRequested)
                return;
            lock (Sync)
            {
                if (dirty)
                    WriteUnlocked();
            }
        }, TaskScheduler.Default);
    }

    private static void EnsureLoaded()
    {
        if (loaded)
            return;
        loaded = true;
        try
        {
            if (!File.Exists(FilePath))
                return;
            var data = JsonConvert.DeserializeObject<DownloadQueueSnapshot>(File.ReadAllText(FilePath));
            jobs = data?.Jobs ?? [];
            pending = data?.Pending ?? [];
        }
        catch
        {
            jobs = [];
            pending = [];
        }
    }

    private static void WriteUnlocked()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonConvert.SerializeObject(new DownloadQueueSnapshot
            {
                Jobs = jobs,
                Pending = pending
            }, Formatting.Indented);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, FilePath, true);
            File.Delete(temp);
            dirty = false;
        }
        catch
        {
        }
    }

    private static PersistedDownloadJob CloneJob(PersistedDownloadJob job) => new()
    {
        Id = job.Id,
        Url = job.Url,
        Title = job.Title,
        SavePath = job.SavePath,
        ImageUrl = job.ImageUrl,
        Settings = job.Settings?.NyClone(),
        DownloadedCount = job.DownloadedCount,
        TotalCount = job.TotalCount,
        NextIndex = job.NextIndex,
        CurrentTitle = job.CurrentTitle,
        CurrentStatus = job.CurrentStatus,
        CurrentProgressPercent = job.CurrentProgressPercent,
        StillDownloading = job.StillDownloading,
        Paused = job.Paused,
        SortOrder = job.SortOrder,
        PausedVideoIds = job.PausedVideoIds?.ToList() ?? []
    };

    private static PersistedPendingLink ClonePending(PersistedPendingLink item) => new()
    {
        Url = item.Url,
        Title = item.Title,
        Settings = item.Settings?.NyClone()
    };
}

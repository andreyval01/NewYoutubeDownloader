namespace YoutubePlaylistDownloader.Utilities;

internal sealed class TranscodePresetSnapshot
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("audioOnly")]
    public bool AudioOnly { get; set; }

    [JsonProperty("videoCodec")]
    public string VideoCodec { get; set; }

    [JsonProperty("crf")]
    public int Crf { get; set; }

    [JsonProperty("maxHeight")]
    public int MaxHeight { get; set; }

    [JsonProperty("audioCodec")]
    public string AudioCodec { get; set; }

    [JsonProperty("audioBitrateKbps")]
    public int AudioBitrateKbps { get; set; }

    [JsonProperty("container")]
    public string Container { get; set; }

    [JsonProperty("signature")]
    public string Signature { get; set; }
}

internal sealed class TranscodeJobFile
{
    [JsonProperty("source")]
    public string Source { get; set; }

    [JsonProperty("output")]
    public string Output { get; set; }

    [JsonProperty("signature")]
    public string Signature { get; set; }

    [JsonProperty("state")]
    public string State { get; set; }

    [JsonProperty("progress")]
    public int Progress { get; set; }

    [JsonProperty("error")]
    public string Error { get; set; }

    [JsonProperty("outputBytes")]
    public long OutputBytes { get; set; }
}

internal sealed class TranscodeJob
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("createdUtc")]
    public string CreatedUtc { get; set; }

    [JsonProperty("updatedUtc")]
    public string UpdatedUtc { get; set; }

    [JsonProperty("preset")]
    public TranscodePresetSnapshot Preset { get; set; }

    [JsonProperty("outputDirectory")]
    public string OutputDirectory { get; set; }

    [JsonProperty("state")]
    public string State { get; set; }

    [JsonProperty("userPaused")]
    public bool UserPaused { get; set; }

    [JsonProperty("files")]
    public List<TranscodeJobFile> Files { get; set; } = [];
}

internal static class TranscodeJobStore
{
    public const int FinishedLimit = 30;
    static readonly object Sync = new();
    static readonly string FilePath = Path.Combine(AppPaths.AppDataDirectory, "TranscodeJobs.json");
    static readonly JsonSerializerSettings Json = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    static List<TranscodeJob> jobs = [];
    static bool loaded;

    public static IReadOnlyList<TranscodeJob> List()
    {
        lock (Sync)
        {
            EnsureLoaded();
            return jobs.Select(Clone).ToList();
        }
    }

    public static void Upsert(TranscodeJob job, bool flushNow = true)
    {
        if (job == null || string.IsNullOrWhiteSpace(job.Id))
            return;
        lock (Sync)
        {
            EnsureLoaded();
            job.UpdatedUtc = DateTime.UtcNow.ToString("o");
            var index = jobs.FindIndex(item => string.Equals(item.Id, job.Id, StringComparison.OrdinalIgnoreCase));
            var copy = Clone(job);
            if (index >= 0)
                jobs[index] = copy;
            else
                jobs.Add(copy);
            TrimUnlocked();
            if (flushNow)
                WriteUnlocked();
        }
    }

    public static void Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;
        lock (Sync)
        {
            EnsureLoaded();
            if (jobs.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) <= 0)
                return;
            WriteUnlocked();
        }
    }

    public static void Reconcile(TranscodeJob job, string missingSource)
    {
        if (job?.Files == null)
            return;
        foreach (var file in job.Files)
        {
            if (file == null)
                continue;
            if (string.IsNullOrWhiteSpace(file.Source) || !File.Exists(file.Source))
            {
                file.State = "failed";
                file.Error = Trim(string.IsNullOrWhiteSpace(missingSource) ? "Source file was not found." : missingSource);
                file.OutputBytes = 0;
                continue;
            }

            var temp = (file.Output ?? "") + ".transcode.tmp";
            if (!string.IsNullOrWhiteSpace(file.Output) && File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
                if (file.State is not ("done" or "skipped" or "cancelled"))
                    file.State = "paused";
            }

            var outputBytes = OutputLength(file.Output);
            file.OutputBytes = outputBytes;
            var matched = outputBytes > 0 && TranscodePlanner.AlreadyDone(file.Output, file.Signature);
            if (matched)
            {
                file.State = "done";
                file.Error = "";
                file.Progress = 100;
                continue;
            }

            if (file.State == "done")
                file.State = "paused";
            else if (file.State == "working")
                file.State = "paused";
        }

        if (job.UserPaused)
        {
            job.State = "paused";
            return;
        }

        var states = job.Files.Select(file => file?.State ?? "").ToList();
        if (states.Count > 0 && states.All(state => state is "done" or "skipped"))
            job.State = "completed";
        else if (states.Count > 0 && states.All(state => state is "done" or "skipped" or "cancelled"))
            job.State = "cancelled";
        else if (job.State is "running" or "queued" or "completed")
            job.State = "paused";
    }

    public static bool CanResume(TranscodeJob job)
    {
        if (job == null || job.State is "completed" or "cancelled")
            return false;
        return job.Files?.Any(file => file != null && file.State is "pending" or "paused" or "failed" or "working") == true;
    }

    public static int CountAutoResume()
    {
        lock (Sync)
        {
            EnsureLoaded();
            return jobs.Count(IsAutoResume);
        }
    }

    public static bool IsAutoResume(TranscodeJob job)
        => job != null
           && !job.UserPaused
           && job.State is "queued" or "running" or "paused"
           && CanResume(job);

    public static TranscodePreset ToPreset(TranscodePresetSnapshot snapshot)
    {
        snapshot ??= new TranscodePresetSnapshot();
        return new TranscodePreset
        {
            Id = string.IsNullOrWhiteSpace(snapshot.Id) ? "custom" : snapshot.Id,
            Title = snapshot.Name ?? "",
            AudioOnly = snapshot.AudioOnly,
            VideoCodec = snapshot.VideoCodec,
            Crf = snapshot.Crf,
            MaxHeight = snapshot.MaxHeight,
            AudioCodec = snapshot.AudioCodec,
            AudioBitrateKbps = snapshot.AudioBitrateKbps,
            Container = snapshot.Container
        };
    }

    public static TranscodePresetSnapshot FromPreset(TranscodePreset preset, string name)
    {
        preset ??= new TranscodePreset { Id = "custom", Title = name };
        return new TranscodePresetSnapshot
        {
            Id = preset.Id,
            Name = string.IsNullOrWhiteSpace(name) ? preset.Title : name,
            AudioOnly = preset.AudioOnly,
            VideoCodec = preset.VideoCodec,
            Crf = preset.Crf,
            MaxHeight = preset.MaxHeight,
            AudioCodec = preset.AudioCodec,
            AudioBitrateKbps = preset.AudioBitrateKbps,
            Container = preset.Container,
            Signature = preset.Signature
        };
    }

    static long OutputLength(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    static string Trim(string text)
    {
        text = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > 180 ? text[..180] : text;
    }

    static void TrimUnlocked()
    {
        var unfinished = jobs.Where(job => job.State is not ("completed" or "cancelled")).ToList();
        var finished = jobs
            .Where(job => job.State is "completed" or "cancelled")
            .OrderByDescending(job => job.UpdatedUtc ?? job.CreatedUtc ?? "")
            .ToList();
        var room = Math.Max(0, FinishedLimit - unfinished.Count);
        var kept = unfinished.Concat(finished.Take(room)).ToList();
        jobs = kept
            .OrderBy(job => job.CreatedUtc ?? "")
            .ToList();
    }

    static void EnsureLoaded()
    {
        if (loaded)
            return;
        loaded = true;
        if (!File.Exists(FilePath))
        {
            jobs = [];
            return;
        }

        try
        {
            var data = JsonConvert.DeserializeObject<JobFile>(File.ReadAllText(FilePath));
            jobs = data?.Jobs?.Where(job => job != null && !string.IsNullOrWhiteSpace(job.Id)).ToList() ?? [];
            foreach (var job in jobs)
                job.Files ??= [];
        }
        catch (Exception ex)
        {
            try
            {
                var backup = FilePath + ".bak";
                if (!File.Exists(backup))
                    File.Copy(FilePath, backup);
            }
            catch
            {
            }

            jobs = [];
            GlobalConsts.NyLog(ex.ToString(), "TranscodeJobs").Wait();
        }
    }

    static void WriteUnlocked()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var json = JsonConvert.SerializeObject(new JobFile { Jobs = jobs }, Json);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(FilePath))
                File.Replace(temp, FilePath, null);
            else
                File.Move(temp, FilePath);
        }
        catch (Exception ex)
        {
            GlobalConsts.NyLog(ex.ToString(), "TranscodeJobs write").Wait();
        }
    }

    static TranscodeJob Clone(TranscodeJob job) => new()
    {
        Id = job.Id,
        CreatedUtc = job.CreatedUtc,
        UpdatedUtc = job.UpdatedUtc,
        Preset = job.Preset == null ? null : new TranscodePresetSnapshot
        {
            Id = job.Preset.Id,
            Name = job.Preset.Name,
            AudioOnly = job.Preset.AudioOnly,
            VideoCodec = job.Preset.VideoCodec,
            Crf = job.Preset.Crf,
            MaxHeight = job.Preset.MaxHeight,
            AudioCodec = job.Preset.AudioCodec,
            AudioBitrateKbps = job.Preset.AudioBitrateKbps,
            Container = job.Preset.Container,
            Signature = job.Preset.Signature
        },
        OutputDirectory = job.OutputDirectory ?? "",
        State = job.State,
        UserPaused = job.UserPaused,
        Files = job.Files?.Select(file => new TranscodeJobFile
        {
            Source = file.Source,
            Output = file.Output,
            Signature = file.Signature,
            State = file.State,
            Progress = file.Progress,
            Error = file.Error,
            OutputBytes = file.OutputBytes
        }).ToList() ?? []
    };

    sealed class JobFile
    {
        [JsonProperty("jobs")]
        public List<TranscodeJob> Jobs { get; set; } = [];
    }
}

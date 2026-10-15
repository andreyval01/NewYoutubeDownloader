namespace YoutubePlaylistDownloader.Utilities;

internal sealed class UserTranscodePreset
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("videoCodec")]
    public string VideoCodec { get; set; }

    [JsonProperty("maxHeight")]
    public int MaxHeight { get; set; }

    [JsonProperty("crf")]
    public int Crf { get; set; }

    [JsonProperty("audioCodec")]
    public string AudioCodec { get; set; }

    [JsonProperty("audioBitrateKbps")]
    public int AudioBitrateKbps { get; set; }

    [JsonProperty("container")]
    public string Container { get; set; }

    [JsonProperty("audioOnly")]
    public bool AudioOnly { get; set; }
}

internal static class TranscodePresetStore
{
    static readonly object Sync = new();
    static readonly string FilePath = Path.Combine(AppPaths.AppDataDirectory, "TranscodePresets.json");
    static readonly JsonSerializerSettings Json = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    static List<UserTranscodePreset> presets = [];
    static bool loaded;

    public static IReadOnlyList<UserTranscodePreset> Load()
    {
        lock (Sync)
        {
            EnsureLoaded();
            return presets.Select(Clone).ToList();
        }
    }

    public static void SaveAll(IReadOnlyList<UserTranscodePreset> items)
    {
        lock (Sync)
        {
            EnsureLoaded();
            presets = (items ?? []).Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id)).Select(Clone).ToList();
            WriteUnlocked();
        }
    }

    static void EnsureLoaded()
    {
        if (loaded)
            return;
        loaded = true;
        if (!File.Exists(FilePath))
        {
            presets = [];
            return;
        }

        try
        {
            var data = JsonConvert.DeserializeObject<PresetFile>(File.ReadAllText(FilePath));
            presets = data?.Presets?.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id)).ToList() ?? [];
        }
        catch (Exception ex)
        {
            KeepBrokenFile();
            presets = [];
            GlobalConsts.NyLog(ex.ToString(), "TranscodePresets").Wait();
        }
    }

    static void WriteUnlocked()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var json = JsonConvert.SerializeObject(new PresetFile { Presets = presets }, Json);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(FilePath))
                File.Replace(temp, FilePath, null);
            else
                File.Move(temp, FilePath);
        }
        catch (Exception ex)
        {
            GlobalConsts.NyLog(ex.ToString(), "TranscodePresets write").Wait();
        }
    }

    static void KeepBrokenFile()
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
    }

    static UserTranscodePreset Clone(UserTranscodePreset item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        VideoCodec = item.VideoCodec,
        MaxHeight = item.MaxHeight,
        Crf = item.Crf,
        AudioCodec = item.AudioCodec,
        AudioBitrateKbps = item.AudioBitrateKbps,
        Container = item.Container,
        AudioOnly = item.AudioOnly
    };

    sealed class PresetFile
    {
        [JsonProperty("presets")]
        public List<UserTranscodePreset> Presets { get; set; } = [];
    }
}

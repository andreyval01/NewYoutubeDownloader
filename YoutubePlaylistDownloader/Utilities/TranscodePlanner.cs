using System.Globalization;

namespace YoutubePlaylistDownloader.Utilities;

internal sealed class MediaFacts
{
    public string Path { get; init; }
    public long Length { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public bool ProbeOk { get; init; }
    public TimeSpan Duration { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string VideoCodec { get; init; }
    public string AudioCodec { get; init; }
    public long? VideoBitrate { get; init; }
    public long? AudioBitrate { get; init; }
    public bool HasVideo => Width > 0 && Height > 0 && !string.IsNullOrWhiteSpace(VideoCodec);
    public bool HasAudio => !string.IsNullOrWhiteSpace(AudioCodec);
}

internal sealed class TranscodePreset
{
    public string Id { get; init; }
    public string Title { get; init; }
    public string Note { get; init; }
    public bool AudioOnly { get; init; }
    public bool KeepOriginal { get; init; }
    public string VideoCodec { get; init; }
    public int Crf { get; init; }
    public int MaxHeight { get; init; }
    public string AudioCodec { get; init; }
    public int AudioBitrateKbps { get; init; }
    public string Container { get; init; }

    public string Signature => KeepOriginal
        ? "as-is"
        : string.Join('|', Id, VideoCodec, Crf, MaxHeight, AudioCodec, AudioBitrateKbps, Container, AudioOnly ? "audio" : "video");

    public bool AppliesTo(MediaFacts facts)
    {
        if (facts == null)
            return false;
        if (KeepOriginal)
            return true;
        if (AudioOnly)
            return facts.HasAudio;
        return facts.HasVideo;
    }
}

internal static class TranscodePlanner
{
    static readonly Regex DurationLine = new(@"Duration:\s*(?<h>\d+):(?<m>\d+):(?<s>\d+(?:\.\d+)?)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly Regex FormatBitrate = new(@"bitrate:\s*(?<br>\d+)\s*kb/s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly Regex VideoLine = new(@"Video:\s+(?<codec>[A-Za-z0-9]+).*?(?<w>\d{2,5})x(?<h>\d{2,5})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly Regex AudioLine = new(@"Audio:\s+(?<codec>[A-Za-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly Regex StreamBitrate = new(@"(?<br>\d+)\s*kb/s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly SemaphoreSlim ProbeGate = new(4, 4);
    static readonly Dictionary<string, MediaFacts> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".webm", ".m4v", ".m4a", ".mp3", ".aac", ".opus", ".flac", ".wav", ".ogg"
    };

    public static bool IsMediaFile(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (name.Contains(".part", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".temp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".incomplete", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".nyd-bak", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".nyd-transcode.json", StringComparison.OrdinalIgnoreCase))
            return false;
        return MediaExtensions.Contains(Path.GetExtension(name));
    }

    public static bool IsStillDownloading(string path)
    {
        var directory = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(name) || !Directory.Exists(directory))
            return false;
        if (File.Exists(path + ".part") || File.Exists(path + ".ytdl"))
            return true;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, name + "*"))
            {
                var candidate = Path.GetFileName(file);
                if (candidate.Contains(".part", StringComparison.OrdinalIgnoreCase)
                    || candidate.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public static IReadOnlyList<TranscodePreset> BuiltIn(Func<string, string> text)
    {
        text ??= static key => key;
        return
        [
            new TranscodePreset { Id = "as-is", Title = text("TranscodeAsIs"), KeepOriginal = true },
            new TranscodePreset { Id = "compatible", Title = text("TranscodeCompatible"), VideoCodec = "libx264", Crf = 23, AudioCodec = "aac", AudioBitrateKbps = 128, Container = "mp4" },
            new TranscodePreset { Id = "smaller", Title = text("TranscodeSmaller"), VideoCodec = "libx264", Crf = 28, MaxHeight = 720, AudioCodec = "aac", AudioBitrateKbps = 96, Container = "mp4" },
            new TranscodePreset { Id = "strong", Title = text("TranscodeStrong"), Note = text("TranscodeStrongNote"), VideoCodec = "libx265", Crf = 30, MaxHeight = 720, AudioCodec = "aac", AudioBitrateKbps = 64, Container = "mp4" },
            new TranscodePreset { Id = "opus", Title = text("TranscodeOpus"), AudioOnly = true, AudioCodec = "libopus", AudioBitrateKbps = 64, Container = "ogg" },
            new TranscodePreset { Id = "mp3", Title = text("TranscodeMp3"), AudioOnly = true, AudioCodec = "libmp3lame", AudioBitrateKbps = 128, Container = "mp3" },
            new TranscodePreset { Id = "custom", Title = text("TranscodeCustom"), VideoCodec = "libx264", Crf = 28, AudioCodec = "aac", AudioBitrateKbps = 128, Container = "mp4" }
        ];
    }

    public static long? EstimateBytes(MediaFacts facts, TranscodePreset preset)
    {
        if (facts == null || preset == null || !facts.ProbeOk || facts.Duration.TotalSeconds < 1 || !preset.AppliesTo(facts))
            return null;
        if (preset.KeepOriginal)
            return facts.Length;

        var seconds = facts.Duration.TotalSeconds;
        var audioBytes = facts.HasAudio && preset.AudioBitrateKbps > 0
            ? preset.AudioBitrateKbps * 1000d / 8d * seconds
            : 0d;
        if (preset.AudioOnly || !facts.HasVideo)
            return Math.Max(1, (long)(audioBytes * 1.02));

        var sourceVideoBps = facts.VideoBitrate ?? 0;
        if (sourceVideoBps <= 0 && facts.Length > 0)
        {
            var knownAudio = facts.AudioBitrate is > 0 ? facts.AudioBitrate.Value / 8d * seconds : 0;
            var videoBytes = Math.Max(0, facts.Length - knownAudio);
            sourceVideoBps = (long)(videoBytes * 8d / seconds);
        }

        if (sourceVideoBps <= 0)
            return null;

        var scale = 1d;
        if (preset.MaxHeight > 0 && facts.Height > preset.MaxHeight && facts.Width > 0)
        {
            var targetWidth = Math.Max(2, facts.Width * preset.MaxHeight / facts.Height);
            var targetPixels = (double)targetWidth * preset.MaxHeight;
            var sourcePixels = (double)facts.Width * facts.Height;
            if (sourcePixels > 0)
                scale = Math.Min(1d, targetPixels / sourcePixels);
        }

        var crfFactor = Math.Pow(2, (23d - preset.Crf) / 6d);
        var codecFactor = CodecFactor(facts.VideoCodec, preset.VideoCodec);
        var videoBps = Math.Max(80_000d, sourceVideoBps * scale * crfFactor * codecFactor);
        return Math.Max(1, (long)((videoBps / 8d * seconds + audioBytes) * 1.02));
    }

    static double CodecFactor(string sourceCodec, string targetCodec)
    {
        var sourceHevc = IsHevc(sourceCodec);
        var targetHevc = IsHevc(targetCodec);
        if (targetHevc && !sourceHevc)
            return 0.55;
        if (!targetHevc && sourceHevc)
            return 1.6;
        return 1;
    }

    static bool IsHevc(string codec)
        => codec != null && (codec.Contains("265", StringComparison.OrdinalIgnoreCase)
                             || codec.Contains("hevc", StringComparison.OrdinalIgnoreCase)
                             || codec.Contains("libx265", StringComparison.OrdinalIgnoreCase));

    public static async Task<MediaFacts> ProbeAsync(string path, CancellationToken token)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 1024)
        {
            return new MediaFacts
            {
                Path = path,
                Length = info.Exists ? info.Length : 0,
                LastWriteUtc = info.Exists ? info.LastWriteTimeUtc : default
            };
        }

        var key = path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;
        }

        var facts = await ReadAsync(info, token).ConfigureAwait(false);
        lock (Cache)
            Cache[key] = facts;
        return facts;
    }

    static async Task<MediaFacts> ReadAsync(FileInfo info, CancellationToken token)
    {
        var facts = new MediaFacts
        {
            Path = info.FullName,
            Length = info.Length,
            LastWriteUtc = info.LastWriteTimeUtc
        };
        if (!File.Exists(GlobalConsts.FFmpegFilePath))
            return facts;

        await ProbeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = GlobalConsts.FFmpegFilePath,
                Arguments = "-nostdin -hide_banner -i \"" + info.FullName.Replace("\"", "") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var process = new Process { StartInfo = start };
            if (!process.Start())
                return facts;

            var errorTask = process.StandardError.ReadToEndAsync(token);
            var outputTask = process.StandardOutput.ReadToEndAsync(token);
            var wait = process.WaitForExitAsync(token);
            var done = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(20), token)).ConfigureAwait(false);
            if (done != wait || !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return facts;
            }

            var stderr = await errorTask.ConfigureAwait(false);
            await outputTask.ConfigureAwait(false);
            return Parse(facts, stderr ?? "");
        }
        catch
        {
            return facts;
        }
        finally
        {
            ProbeGate.Release();
        }
    }

    static MediaFacts Parse(MediaFacts seed, string stderr)
    {
        var durationMatch = DurationLine.Match(stderr);
        if (!durationMatch.Success)
            return seed;

        var duration = TimeSpan.FromSeconds(
            int.Parse(durationMatch.Groups["h"].Value, CultureInfo.InvariantCulture) * 3600d
            + int.Parse(durationMatch.Groups["m"].Value, CultureInfo.InvariantCulture) * 60d
            + double.Parse(durationMatch.Groups["s"].Value, CultureInfo.InvariantCulture));

        long? formatBitrate = null;
        var format = FormatBitrate.Match(stderr);
        if (format.Success)
            formatBitrate = long.Parse(format.Groups["br"].Value, CultureInfo.InvariantCulture) * 1000L;

        string videoCodec = null;
        string audioCodec = null;
        var width = 0;
        var height = 0;
        long? videoBitrate = null;
        long? audioBitrate = null;
        foreach (var line in stderr.Split('\n'))
        {
            var video = VideoLine.Match(line);
            if (video.Success && videoCodec == null)
            {
                videoCodec = video.Groups["codec"].Value;
                width = int.Parse(video.Groups["w"].Value, CultureInfo.InvariantCulture);
                height = int.Parse(video.Groups["h"].Value, CultureInfo.InvariantCulture);
                var videoRate = StreamBitrate.Match(line);
                if (videoRate.Success)
                    videoBitrate = long.Parse(videoRate.Groups["br"].Value, CultureInfo.InvariantCulture) * 1000L;
                continue;
            }

            var audio = AudioLine.Match(line);
            if (audio.Success && audioCodec == null)
            {
                audioCodec = audio.Groups["codec"].Value;
                var audioRate = StreamBitrate.Match(line);
                if (audioRate.Success)
                    audioBitrate = long.Parse(audioRate.Groups["br"].Value, CultureInfo.InvariantCulture) * 1000L;
            }
        }

        if (videoBitrate is null && formatBitrate is > 0)
            videoBitrate = Math.Max(0, formatBitrate.Value - (audioBitrate ?? 0));

        return new MediaFacts
        {
            Path = seed.Path,
            Length = seed.Length,
            LastWriteUtc = seed.LastWriteUtc,
            ProbeOk = duration.TotalSeconds >= 1,
            Duration = duration,
            Width = width,
            Height = height,
            VideoCodec = videoCodec,
            AudioCodec = audioCodec,
            VideoBitrate = videoBitrate,
            AudioBitrate = audioBitrate
        };
    }

    public static string OutputPath(string source, TranscodePreset preset)
    {
        if (preset == null || preset.KeepOriginal)
            return source;
        var extension = preset.Container?.Trim().TrimStart('.') ?? "mp4";
        var directory = Path.GetDirectoryName(source) ?? "";
        var name = Path.GetFileNameWithoutExtension(source);
        return Path.Combine(directory, name + "." + extension);
    }

    public static string BuildArguments(MediaFacts facts, TranscodePreset preset, string output)
    {
        var args = new StringBuilder();
        args.Append("-nostdin -hide_banner -y -progress pipe:1 -nostats -i \"")
            .Append(facts.Path.Replace("\"", ""))
            .Append("\" ");

        if (preset.AudioOnly)
        {
            args.Append("-vn -map 0:a:0 -c:a ").Append(preset.AudioCodec)
                .Append(" -b:a ").Append(preset.AudioBitrateKbps).Append("k ");
        }
        else
        {
            args.Append("-map 0:v:0 ");
            if (facts.HasAudio)
                args.Append("-map 0:a:0? ");
            if (preset.MaxHeight > 0 && facts.Height > preset.MaxHeight)
                args.Append("-vf scale=-2:'min(").Append(preset.MaxHeight).Append(",ih)' ");
            args.Append("-c:v ").Append(preset.VideoCodec)
                .Append(" -preset veryfast -crf ").Append(preset.Crf).Append(' ');
            if (IsHevc(preset.VideoCodec))
                args.Append("-tag:v hvc1 ");
            if (facts.HasAudio)
                args.Append("-c:a ").Append(preset.AudioCodec).Append(" -b:a ").Append(preset.AudioBitrateKbps).Append("k ");
            if (string.Equals(preset.Container, "mp4", StringComparison.OrdinalIgnoreCase))
                args.Append("-movflags +faststart ");
        }

        args.Append('"').Append(output.Replace("\"", "")).Append('"');
        return args.ToString();
    }

    public static bool AlreadyDone(string path, string signature)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(signature))
            return false;
        var mark = path + ".nyd-transcode.json";
        if (!File.Exists(mark))
            return false;
        try
        {
            var text = File.ReadAllText(mark);
            return text.Contains("\"signature\":\"" + signature + "\"", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static void WriteMark(string path, string signature)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["signature"] = signature,
            ["utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        });
        File.WriteAllText(path + ".nyd-transcode.json", json);
    }

    public static void Commit(string source, string temp, string finalPath)
    {
        var produced = new FileInfo(temp);
        if (!produced.Exists || produced.Length <= 0)
            throw new InvalidOperationException("ffmpeg produced an empty file.");

        var backup = source + ".nyd-bak";
        if (File.Exists(backup))
            File.Delete(backup);
        File.Move(source, backup);
        try
        {
            if (!string.Equals(finalPath, source, StringComparison.OrdinalIgnoreCase) && File.Exists(finalPath))
                File.Delete(finalPath);
            var directory = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.Move(temp, finalPath);
            if (!File.Exists(finalPath) || new FileInfo(finalPath).Length <= 0)
                throw new InvalidOperationException("Replaced file is empty.");
            File.Delete(backup);
            var oldMark = source + ".nyd-transcode.json";
            if (!string.Equals(source, finalPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldMark))
                File.Delete(oldMark);
        }
        catch
        {
            if (!File.Exists(source) && File.Exists(backup))
            {
                try { File.Move(backup, source); } catch { }
            }

            throw;
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 0)
            return "";
        if (bytes >= 1L << 30)
            return (bytes / (double)(1L << 30)).ToString("0.00", CultureInfo.CurrentCulture) + " GiB";
        if (bytes >= 1L << 20)
            return (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.CurrentCulture) + " MiB";
        if (bytes >= 1L << 10)
            return (bytes / (double)(1L << 10)).ToString("0", CultureInfo.CurrentCulture) + " KiB";
        return bytes.ToString(CultureInfo.CurrentCulture) + " B";
    }

    public static string FormatDelta(long current, long estimate)
    {
        var delta = estimate - current;
        var sign = delta > 0 ? "+" : delta < 0 ? "−" : "";
        return sign + FormatSize(Math.Abs(delta));
    }

    public static string FormatPercent(long current, long estimate)
    {
        if (current <= 0)
            return "";
        var percent = (current - estimate) * 100d / current;
        var sign = percent > 0 ? "−" : percent < 0 ? "+" : "";
        return sign + Math.Abs(percent).ToString("0", CultureInfo.CurrentCulture) + "%";
    }
}

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
        ".mp4", ".mkv", ".webm", ".m4v", ".mov", ".3gp", ".3g2", ".avi", ".mpg", ".mpeg", ".ts", ".wmv",
        ".m4a", ".mp3", ".aac", ".opus", ".flac", ".wav", ".ogg", ".mp2", ".wma"
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

    public static bool IsCopyCodec(string codec)
        => string.IsNullOrWhiteSpace(codec) || codec.Equals("copy", StringComparison.OrdinalIgnoreCase);

    public static bool IsLosslessAudio(string codec)
        => codec != null && codec.Contains("flac", StringComparison.OrdinalIgnoreCase);

    public static long? EstimateBytes(MediaFacts facts, TranscodePreset preset)
    {
        if (facts == null || preset == null || !facts.ProbeOk || facts.Duration.TotalSeconds < 1 || !preset.AppliesTo(facts))
            return null;
        if (preset.KeepOriginal)
            return facts.Length;

        var seconds = facts.Duration.TotalSeconds;
        var audioBytes = AudioBytes(facts, preset, seconds);
        if (preset.AudioOnly || !facts.HasVideo)
            return Math.Max(1, (long)(audioBytes * 1.02));

        if (IsCopyCodec(preset.VideoCodec))
        {
            if (IsCopyCodec(preset.AudioCodec) || !facts.HasAudio)
                return facts.Length;
            var oldAudio = facts.AudioBitrate is > 0 ? facts.AudioBitrate.Value / 8d * seconds : 0d;
            var videoBytes = Math.Max(facts.Length * 0.5d, facts.Length - oldAudio);
            return Math.Max(1, (long)((videoBytes + audioBytes) * 1.02));
        }

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

        var crf = preset.Crf > 0 ? preset.Crf : 23;
        var crfFactor = Math.Pow(2, (23d - crf) / 6d);
        var codecFactor = CodecFactor(facts.VideoCodec, preset.VideoCodec);
        var videoBps = Math.Max(80_000d, sourceVideoBps * scale * crfFactor * codecFactor);
        var mixedAudio = IsCopyCodec(preset.AudioCodec)
            ? (facts.AudioBitrate is > 0 ? facts.AudioBitrate.Value / 8d * seconds : audioBytes)
            : audioBytes;
        return Math.Max(1, (long)((videoBps / 8d * seconds + mixedAudio) * 1.02));
    }

    static double AudioBytes(MediaFacts facts, TranscodePreset preset, double seconds)
    {
        if (!facts.HasAudio)
            return 0;
        if (IsCopyCodec(preset.AudioCodec))
            return facts.AudioBitrate is > 0 ? facts.AudioBitrate.Value / 8d * seconds : facts.Length * 0.1d;
        var kbps = IsLosslessAudio(preset.AudioCodec) ? 900 : Math.Max(32, preset.AudioBitrateKbps);
        return kbps * 1000d / 8d * seconds;
    }

    static double CodecFactor(string sourceCodec, string targetCodec)
    {
        var source = CodecEfficiency(sourceCodec);
        var target = CodecEfficiency(targetCodec);
        if (source <= 0)
            return 1;
        return target / source;
    }

    static double CodecEfficiency(string codec)
    {
        if (IsAv1(codec))
            return 0.45;
        if (IsHevc(codec))
            return 0.55;
        if (IsVp9(codec))
            return 0.7;
        if (IsMpeg2(codec))
            return 1.7;
        if (IsWmv(codec))
            return 1.5;
        if (IsMpeg4Part2(codec))
            return 1.35;
        return 1;
    }

    static bool IsHevc(string codec)
        => codec != null && (codec.Contains("265", StringComparison.OrdinalIgnoreCase)
                             || codec.Contains("hevc", StringComparison.OrdinalIgnoreCase)
                             || codec.Contains("libx265", StringComparison.OrdinalIgnoreCase));

    static bool IsAv1(string codec)
        => codec != null && (codec.Contains("av1", StringComparison.OrdinalIgnoreCase)
                             || codec.Contains("libaom", StringComparison.OrdinalIgnoreCase));

    static bool IsVp9(string codec)
        => codec != null && codec.Contains("vp9", StringComparison.OrdinalIgnoreCase);

    static bool IsAac(string codec)
        => codec != null && (codec.Contains("aac", StringComparison.OrdinalIgnoreCase)
                             || codec.Contains("mp4a", StringComparison.OrdinalIgnoreCase));

    static bool IsMp3(string codec)
        => codec != null && codec.Contains("mp3", StringComparison.OrdinalIgnoreCase);

    static bool IsOpus(string codec)
        => codec != null && codec.Contains("opus", StringComparison.OrdinalIgnoreCase);

    static bool IsVorbis(string codec)
        => codec != null && codec.Contains("vorbis", StringComparison.OrdinalIgnoreCase);

    static bool IsMp2(string codec)
        => codec != null && codec.Contains("mp2", StringComparison.OrdinalIgnoreCase);

    static bool IsWma(string codec)
        => codec != null && codec.Contains("wma", StringComparison.OrdinalIgnoreCase);

    static bool IsMpeg2(string codec)
        => codec != null && codec.Contains("mpeg2", StringComparison.OrdinalIgnoreCase);

    static bool IsWmv(string codec)
        => codec != null && codec.Contains("wmv", StringComparison.OrdinalIgnoreCase);

    static bool IsMpeg4Part2(string codec)
    {
        if (string.IsNullOrWhiteSpace(codec) || IsH264Name(codec) || IsHevc(codec) || IsAv1(codec) || IsVp9(codec) || IsMpeg2(codec) || IsWmv(codec))
            return false;
        return codec.Contains("mpeg4", StringComparison.OrdinalIgnoreCase)
               || codec.Contains("xvid", StringComparison.OrdinalIgnoreCase)
               || codec.Contains("h263", StringComparison.OrdinalIgnoreCase)
               || codec.Contains("msmpeg4", StringComparison.OrdinalIgnoreCase);
    }

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

        if (audioBitrate is null && audioCodec != null && videoCodec == null && formatBitrate is > 0)
            audioBitrate = formatBitrate;
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

    public static string ContainerFor(MediaFacts facts, TranscodePreset preset)
    {
        if (preset == null || preset.KeepOriginal)
            return ExtensionOf(facts?.Path) ?? "mp4";

        var requested = CanonicalContainer(preset.Container);
        if (preset.AudioOnly)
            return AudioContainer(requested, preset.AudioCodec, facts);
        if (requested == "mkv")
            return "mkv";
        if (VideoContainerFits(requested, preset, facts))
            return requested;
        return "mkv";
    }

    public static string MarkSignature(TranscodePreset preset, MediaFacts facts)
    {
        if (preset == null || preset.KeepOriginal)
            return "as-is";
        return string.Join('|',
            preset.Id,
            preset.VideoCodec,
            preset.Crf,
            preset.MaxHeight,
            preset.AudioCodec,
            preset.AudioBitrateKbps,
            ContainerFor(facts, preset),
            preset.AudioOnly ? "audio" : "video");
    }

    public static string OutputPath(string source, TranscodePreset preset, MediaFacts facts = null, string outputDirectory = null)
    {
        if (preset == null || preset.KeepOriginal)
            return source;
        var extension = ContainerFor(facts, preset);
        var name = Path.GetFileNameWithoutExtension(source);
        var sourceDirectory = Path.GetDirectoryName(source) ?? "";
        var directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(sourceDirectory, "transcoding")
            : outputDirectory;
        var path = Path.Combine(directory, name + "." + extension);
        if (PathsEqual(path, source))
            path = Path.Combine(sourceDirectory, "transcoding", name + "." + extension);
        return path;
    }

    static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    public static string BuildArguments(MediaFacts facts, TranscodePreset preset, string output)
    {
        var args = new StringBuilder();
        args.Append("-nostdin -hide_banner -y -progress pipe:1 -nostats -i \"")
            .Append(facts.Path.Replace("\"", ""))
            .Append("\" ");

        if (preset.AudioOnly)
        {
            args.Append("-vn -map 0:a:0 ");
            AppendAudio(args, preset);
        }
        else
        {
            args.Append("-map 0:v:0 ");
            if (facts.HasAudio)
                args.Append("-map 0:a:0? ");
            if (!IsCopyCodec(preset.VideoCodec))
            {
                var cap = preset.MaxHeight;
                if (CanonicalExtension(output) == "3gp")
                    cap = cap <= 0 ? 480 : Math.Min(cap, 480);
                if (cap > 0 && facts.Height > cap)
                    args.Append("-vf scale=-2:'min(").Append(cap).Append(",ih)' ");
                AppendVideo(args, preset, CanonicalExtension(output), facts);
            }
            else
            {
                args.Append("-c:v copy ");
            }

            if (facts.HasAudio)
                AppendAudio(args, preset);
        }

        var extension = CanonicalExtension(output);
        if (extension is "mp4" or "m4a" or "mov" or "m4v" or "3gp")
            args.Append("-movflags +faststart ");
        var muxer = MuxerFor(extension);
        if (!string.IsNullOrEmpty(muxer))
            args.Append("-f ").Append(muxer).Append(' ');

        args.Append('"').Append(output.Replace("\"", "")).Append('"');
        return args.ToString();
    }

    static void AppendVideo(StringBuilder args, TranscodePreset preset, string extension, MediaFacts facts)
    {
        var codec = string.IsNullOrWhiteSpace(preset.VideoCodec) ? "libx264" : preset.VideoCodec;
        var crf = preset.Crf > 0 ? preset.Crf : 28;
        args.Append("-c:v ").Append(codec).Append(' ');
        if (codec.Equals("libvpx-vp9", StringComparison.OrdinalIgnoreCase))
            args.Append("-deadline good -cpu-used 4 -row-mt 1 -b:v 0 -crf ").Append(crf).Append(' ');
        else if (codec.Equals("libaom-av1", StringComparison.OrdinalIgnoreCase))
            args.Append("-cpu-used 8 -row-mt 1 -b:v 0 -crf ").Append(crf).Append(' ');
        else if (codec.Equals("libxvid", StringComparison.OrdinalIgnoreCase))
            args.Append("-q:v ").Append(QScale(crf, 12, 2, 2, 15)).Append(" -vtag XVID ");
        else if (codec.Equals("mpeg2video", StringComparison.OrdinalIgnoreCase))
            args.Append("-q:v ").Append(QScale(crf, 10, 3, 2, 12)).Append(' ');
        else if (codec.Equals("wmv2", StringComparison.OrdinalIgnoreCase))
            args.Append("-q:v ").Append(QScale(crf, 10, 2, 2, 16)).Append(' ');
        else if (codec.Equals("libx264", StringComparison.OrdinalIgnoreCase))
        {
            args.Append("-preset veryfast -crf ").Append(crf).Append(' ');
            if (extension == "3gp")
                args.Append("-profile:v baseline -level 3.0 ");
            else
            {
                args.Append("-profile:v high ");
                var target = TargetHeight(facts, preset);
                if (target > 0 && target <= 1080)
                    args.Append("-level 4.1 ");
                else if (target > 1080)
                    args.Append("-level 5.1 ");
            }
        }
        else if (codec.Equals("libx265", StringComparison.OrdinalIgnoreCase))
            args.Append("-preset veryfast -crf ").Append(crf).Append(' ');
        else
            args.Append("-preset veryfast -crf ").Append(crf).Append(' ');
        args.Append("-pix_fmt yuv420p ");
        if (IsHevc(codec))
            args.Append("-tag:v hvc1 ");
    }

    static void AppendAudio(StringBuilder args, TranscodePreset preset)
    {
        if (IsCopyCodec(preset.AudioCodec))
        {
            args.Append("-c:a copy ");
            return;
        }

        var codec = string.IsNullOrWhiteSpace(preset.AudioCodec) ? "aac" : preset.AudioCodec;
        args.Append("-c:a ").Append(codec).Append(' ');
        if (codec.Equals("mp2", StringComparison.OrdinalIgnoreCase))
            args.Append("-ar 44100 ");
        if (!IsLosslessAudio(codec) && preset.AudioBitrateKbps > 0)
            args.Append("-b:a ").Append(preset.AudioBitrateKbps).Append("k ");
    }

    static int QScale(int crf, int subtract, int divisor, int min, int max)
    {
        var value = (int)Math.Round((crf - subtract) / (double)divisor, MidpointRounding.AwayFromZero);
        return Math.Clamp(value, min, max);
    }

    static int TargetHeight(MediaFacts facts, TranscodePreset preset)
    {
        var height = facts?.Height ?? 0;
        if (height <= 0)
            return preset.MaxHeight;
        if (preset.MaxHeight > 0 && height > preset.MaxHeight)
            return preset.MaxHeight;
        return height;
    }

    static string AudioContainer(string requested, string codec, MediaFacts facts)
    {
        if (requested is "mkv" or "mka")
            return "mkv";
        if (IsCopyCodec(codec))
        {
            var source = facts?.AudioCodec ?? "";
            if (IsMp3(source))
                return requested == "mp3" ? "mp3" : "mkv";
            if (IsOpus(source) || IsVorbis(source))
                return requested is "ogg" or "opus" ? "ogg" : "mkv";
            if (IsLosslessAudio(source))
                return requested == "flac" ? "flac" : "mkv";
            if (IsMp2(source))
                return requested == "mp2" ? "mp2" : "mkv";
            if (IsWma(source))
                return requested == "wma" ? "wma" : "mkv";
            return requested is "m4a" or "mp4" ? "m4a" : "mkv";
        }

        return codec switch
        {
            "libmp3lame" => requested == "mkv" ? "mkv" : "mp3",
            "libopus" => requested == "mkv" ? "mkv" : "ogg",
            "flac" => requested == "mkv" ? "mkv" : "flac",
            "mp2" => requested == "mkv" ? "mkv" : "mp2",
            "wmav2" => requested == "mkv" ? "mkv" : "wma",
            _ => requested == "mkv" ? "mkv" : "m4a"
        };
    }

    static bool VideoContainerFits(string container, TranscodePreset preset, MediaFacts facts)
    {
        var video = preset.VideoCodec ?? "";
        var audio = preset.AudioCodec ?? "";
        return container switch
        {
            "mp4" => VideoFitsMp4(video, facts, allowAv1: true) && AudioFitsMp4(audio, facts),
            "mov" or "m4v" => VideoFitsMp4(video, facts, allowAv1: false) && AudioFitsMp4(audio, facts),
            "3gp" => video.Equals("libx264", StringComparison.OrdinalIgnoreCase) && AudioIs(audio, facts, aac: true),
            "avi" => video.Equals("libxvid", StringComparison.OrdinalIgnoreCase) && AudioIs(audio, facts, mp3: true),
            "mpg" => video.Equals("mpeg2video", StringComparison.OrdinalIgnoreCase) && AudioIs(audio, facts, mp2: true),
            "ts" => VideoFitsTs(video, facts) && AudioFitsTs(audio, facts),
            "wmv" => video.Equals("wmv2", StringComparison.OrdinalIgnoreCase) && AudioIs(audio, facts, wma: true),
            "webm" => WebmOk(preset, facts),
            _ => false
        };
    }

    static bool VideoFitsMp4(string codec, MediaFacts facts, bool allowAv1)
    {
        if (IsCopyCodec(codec))
        {
            var source = facts?.VideoCodec ?? "";
            if (string.IsNullOrWhiteSpace(source))
                return true;
            if (IsH264Name(source) || IsHevc(source))
                return true;
            return allowAv1 && IsAv1(source);
        }

        if (codec.Equals("libx264", StringComparison.OrdinalIgnoreCase) || codec.Equals("libx265", StringComparison.OrdinalIgnoreCase))
            return true;
        return allowAv1 && codec.Equals("libaom-av1", StringComparison.OrdinalIgnoreCase);
    }

    static bool VideoFitsTs(string codec, MediaFacts facts)
    {
        if (codec.Equals("libx264", StringComparison.OrdinalIgnoreCase)
            || codec.Equals("libx265", StringComparison.OrdinalIgnoreCase)
            || codec.Equals("mpeg2video", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!IsCopyCodec(codec))
            return false;
        var source = facts?.VideoCodec ?? "";
        if (string.IsNullOrWhiteSpace(source))
            return true;
        return IsH264Name(source) || IsHevc(source) || IsMpeg2(source);
    }

    static bool AudioFitsTs(string codec, MediaFacts facts)
    {
        if (facts == null || !facts.HasAudio)
            return true;
        if (codec is "aac" or "libmp3lame" or "mp2")
            return true;
        if (!IsCopyCodec(codec))
            return false;
        var source = facts.AudioCodec ?? "";
        return string.IsNullOrWhiteSpace(source) || IsAac(source) || IsMp3(source) || IsMp2(source);
    }

    static bool AudioIs(string codec, MediaFacts facts, bool aac = false, bool mp3 = false, bool mp2 = false, bool wma = false)
    {
        if (facts == null || !facts.HasAudio)
            return true;
        if (aac && codec.Equals("aac", StringComparison.OrdinalIgnoreCase))
            return true;
        if (mp3 && codec.Equals("libmp3lame", StringComparison.OrdinalIgnoreCase))
            return true;
        if (mp2 && codec.Equals("mp2", StringComparison.OrdinalIgnoreCase))
            return true;
        if (wma && codec.Equals("wmav2", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    static bool AudioFitsMp4(string codec, MediaFacts facts)
    {
        if (facts == null || !facts.HasAudio)
            return true;
        if (IsCopyCodec(codec))
        {
            var source = facts.AudioCodec ?? "";
            return string.IsNullOrWhiteSpace(source) || IsAac(source) || IsMp3(source);
        }

        return codec is "aac" or "libmp3lame";
    }

    static bool WebmOk(TranscodePreset preset, MediaFacts facts)
    {
        var videoOk = preset.VideoCodec is "libvpx-vp9" or "libaom-av1"
                      || (IsCopyCodec(preset.VideoCodec) && (IsVp9(facts?.VideoCodec) || IsAv1(facts?.VideoCodec)));
        if (!videoOk)
            return false;
        if (facts == null || !facts.HasAudio)
            return true;
        if (preset.AudioCodec == "libopus")
            return true;
        return IsCopyCodec(preset.AudioCodec) && (IsOpus(facts.AudioCodec) || IsVorbis(facts.AudioCodec));
    }

    static bool IsH264Name(string codec)
        => codec.Contains("264", StringComparison.OrdinalIgnoreCase) || codec.Contains("avc", StringComparison.OrdinalIgnoreCase);

    static string ExtensionOf(string path)
    {
        var extension = Path.GetExtension(path ?? "").TrimStart('.').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(extension) ? null : extension;
    }

    static string CanonicalContainer(string container)
    {
        var value = (container ?? "").Trim().TrimStart('.').ToLowerInvariant();
        return value switch
        {
            "" => "mp4",
            "mpeg" => "mpg",
            "3g2" => "3gp",
            _ => value
        };
    }

    static string CanonicalExtension(string path)
    {
        var extension = ExtensionOf(path);
        if (extension is "tmp" or "temp")
        {
            var trimmed = StripLastExtension(path);
            extension = ExtensionOf(trimmed);
            if (extension == "transcode")
                extension = ExtensionOf(StripLastExtension(trimmed));
        }

        return CanonicalContainer(extension);
    }

    static string StripLastExtension(string path)
    {
        var dot = (path ?? "").LastIndexOf('.');
        return dot > 0 ? path[..dot] : path ?? "";
    }

    static string MuxerFor(string extension) => extension switch
    {
        "mp4" or "m4v" or "m4a" => "mp4",
        "mov" => "mov",
        "3gp" => "3gp",
        "avi" => "avi",
        "mpg" => "mpeg",
        "ts" => "mpegts",
        "wmv" or "wma" => "asf",
        "mkv" => "matroska",
        "webm" => "webm",
        "mp3" => "mp3",
        "ogg" or "opus" => "ogg",
        "flac" => "flac",
        "mp2" => "mp2",
        "wav" => "wav",
        _ => ""
    };

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

        if (!PathsEqual(source, finalPath))
        {
            var directory = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            if (File.Exists(finalPath))
                File.Delete(finalPath);
            File.Move(temp, finalPath);
            if (!File.Exists(finalPath) || new FileInfo(finalPath).Length <= 0)
                throw new InvalidOperationException("ffmpeg produced an empty file.");
            return;
        }

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

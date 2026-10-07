using Newtonsoft.Json.Linq;

namespace YoutubePlaylistDownloader.Utilities;

internal static partial class YtDlpDownloader
{
    public static string ExePath => Path.Combine(GlobalConsts.CurrentDir, "yt-dlp.exe");

    public static bool IsAvailable => File.Exists(ExePath);

    public static async Task DownloadAsync(
        string videoId,
        string outputFile,
        bool audioOnly,
        string audioFormat,
        string videoFormat,
        IProgress<(int Percent, string Speed)> progress,
        CancellationToken token
    ) => await DownloadAsync(
        videoId,
        outputFile,
        audioOnly,
        audioFormat,
        videoFormat,
        progress,
        token,
        youtubeAccount: true,
        maxHeight: null
    ).ConfigureAwait(false);

    public static async Task DownloadAsync(
        string urlOrId,
        string outputFile,
        bool audioOnly,
        string audioFormat,
        string videoFormat,
        IProgress<(int Percent, string Speed)> progress,
        CancellationToken token,
        bool youtubeAccount,
        int? maxHeight
    )
    {
        if (!IsAvailable)
            throw new FileNotFoundException("yt-dlp.exe was not found.", ExePath);
        if (youtubeAccount && !YoutubeSession.Current.IsSignedIn)
            throw new InvalidOperationException("YouTube session is not signed in.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        var outputDir = Path.GetDirectoryName(outputFile)!;
        var name = Path.GetFileNameWithoutExtension(outputFile);
        var ext = Path.GetExtension(outputFile).TrimStart('.');
        if (string.IsNullOrWhiteSpace(ext))
            ext = audioOnly ? audioFormat : videoFormat;

        var template = Path.Combine(outputDir, name + ".%(ext)s");
        var args = new StringBuilder();
        args.Append("--no-warnings --newline --no-playlist --continue ");
        args.Append("--retries 10 --fragment-retries 10 --file-access-retries 5 ");
        args.Append("-o \"").Append(template).Append("\" ");
        args.Append("--ffmpeg-location \"").Append(GlobalConsts.FFmpegFilePath).Append("\" ");

        AppendCookies(args, urlOrId, youtubeAccount);

        if (audioOnly)
        {
            args.Append("-f ba/b -x --audio-format ").Append(audioFormat).Append(' ');
        }
        else
        {
            if (maxHeight is > 0)
            {
                args.Append("-f \"bv*[height<=").Append(maxHeight.Value)
                    .Append("]+ba/b[height<=").Append(maxHeight.Value)
                    .Append("]/bv*+ba/b\" ");
            }
            else
            {
                args.Append("-f \"bv*[ext=mp4]+ba[ext=m4a]/bv*+ba/b\" ");
            }

            args.Append("--merge-output-format ").Append(ext).Append(' ');
        }

        args.Append("-- \"").Append(urlOrId).Append('"');

        var errors = new StringBuilder();
        var percentRx = PercentRegex();
        var exitCode = await RunAsync(args.ToString(), (line, isError) =>
        {
            if (isError || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                errors.AppendLine(line);
            var match = percentRx.Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value.Split('.')[0], out var pct))
            {
                var speed = match.Groups[2].Success ? match.Groups[2].Value : "";
                progress?.Report((Math.Clamp(pct, 0, 99), speed));
            }
        }, token).ConfigureAwait(false);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errors.ToString())
                    ? $"yt-dlp exited with code {exitCode}."
                    : errors.ToString().Trim()
            );
        }

        var produced = Directory
            .GetFiles(outputDir, name + ".*")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (produced is null)
            throw new InvalidOperationException("yt-dlp did not produce an output file.");

        if (!string.Equals(produced, outputFile, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(outputFile))
                File.Delete(outputFile);
            File.Move(produced, outputFile);
        }

        progress?.Report((100, ""));
    }

    public static async Task<ExternalMediaInfo> GetInfoAsync(string url, CancellationToken token)
    {
        if (!IsAvailable)
            throw new FileNotFoundException("yt-dlp.exe was not found.", ExePath);

        var normalized = MediaSourceHelpers.NormalizeUrl(url);
        var args = new StringBuilder();
        args.Append("--dump-single-json --flat-playlist --skip-download --no-warnings --no-progress ");
        args.Append("--ignore-no-formats-error ");
        AppendCookies(args, normalized, youtubeAccount: false);
        args.Append("-- \"").Append(normalized).Append('"');

        var captured = await CaptureAsync(args.ToString(), token).ConfigureAwait(false);
        var raw = captured.StdOut;
        var jsonStart = raw.IndexOf('{');
        if (jsonStart < 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(captured.StdErr)
                    ? "yt-dlp did not return media information."
                    : captured.StdErr.Trim()
            );
        }

        JObject tokenJson;
        try
        {
            tokenJson = JObject.Parse(raw[jsonStart..]);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not parse media information.", ex);
        }

        var source = DetectSource(tokenJson, normalized);
        var type = ReadString(tokenJson, "_type");
        var isPlaylist = string.Equals(type, "playlist", StringComparison.OrdinalIgnoreCase);

        var videos = new List<GenericVideo>();
        var entries = tokenJson["entries"] as JArray;
        if (entries != null && !string.Equals(type, "video", StringComparison.OrdinalIgnoreCase))
        {
            isPlaylist = true;
            foreach (var entry in entries)
            {
                if (entry is not JObject item)
                    continue;
                try
                {
                    var video = ToVideo(item, source, normalized);
                    if (video != null)
                        videos.Add(video);
                }
                catch
                {
                    // Skip broken playlist items instead of failing the whole lookup.
                }
            }
        }
        else
        {
            var video = ToVideo(tokenJson, source, normalized);
            if (video != null)
                videos.Add(video);
        }

        if (videos.Count == 0)
            throw new InvalidOperationException("No videos were found at this link.");

        var uploader = FirstNonEmpty(
            ReadString(tokenJson, "playlist_uploader"),
            ReadString(tokenJson, "uploader"),
            ReadString(tokenJson, "channel"),
            videos[0].Author.ChannelTitle
        );
        var title = FirstNonEmpty(
            ReadString(tokenJson, "playlist_title"),
            ReadString(tokenJson, "title"),
            uploader,
            source == MediaSourceKind.VkVideo ? "VK Video" : "Rutube"
        );

        if (PlaylistLinkScanner.TryGetRutubePlaylistId(normalized, out var rutubePlaylistId))
        {
            isPlaylist = true;
            var playlistTitle = await PlaylistLinkScanner.TryGetRutubePlaylistTitleAsync(rutubePlaylistId, token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(playlistTitle))
                title = playlistTitle;
        }
        var thumbnail = MediaSourceHelpers.NormalizeThumbnailUrl(
            FirstNonEmpty(ReadString(tokenJson, "thumbnail"), videos[0].Thumbnails.FirstOrDefault()?.Url)
        );

        return new ExternalMediaInfo
        {
            IsPlaylist = isPlaylist,
            Title = title,
            Uploader = uploader,
            Thumbnail = thumbnail,
            ViewCount = ReadInt64(tokenJson, "view_count") ?? videos[0].ViewCount,
            Videos = videos
        };
    }

    private static GenericVideo ToVideo(JObject item, MediaSourceKind source, string fallbackUrl)
    {
        var id = FirstNonEmpty(ReadString(item, "id"), ReadString(item, "display_id"));
        var url = FirstNonEmpty(
            ReadString(item, "webpage_url"),
            ReadString(item, "original_url"),
            ReadString(item, "url")
        );
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            url = source switch
            {
                MediaSourceKind.Rutube when !string.IsNullOrWhiteSpace(id) => $"https://rutube.ru/video/{id}/",
                MediaSourceKind.VkVideo when !string.IsNullOrWhiteSpace(id) => $"https://vk.com/video{id}",
                _ => fallbackUrl
            };
        }

        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(url))
            return null;

        TimeSpan? duration = null;
        var seconds = ReadDouble(item, "duration");
        if (seconds is > 0)
            duration = TimeSpan.FromSeconds(seconds.Value);

        return new GenericVideo(
            id ?? url,
            url,
            FirstNonEmpty(ReadString(item, "title"), id, url),
            FirstNonEmpty(ReadString(item, "uploader"), ReadString(item, "channel"), ReadString(item, "uploader_id")),
            duration,
            ParseThumbnails(item),
            ReadInt64(item, "view_count"),
            source
        );
    }

    private static IReadOnlyList<Thumbnail> ParseThumbnails(JObject item)
    {
        var list = new List<Thumbnail>();
        if (item["thumbnails"] is JArray array)
        {
            foreach (var thumb in array)
            {
                if (thumb is not JObject obj)
                    continue;
                var url = MediaSourceHelpers.NormalizeThumbnailUrl(ReadString(obj, "url"));
                if (string.IsNullOrWhiteSpace(url))
                    continue;
                list.Add(new Thumbnail(url, new Resolution(ReadInt32(obj, "width"), ReadInt32(obj, "height"))));
            }
        }

        var single = MediaSourceHelpers.NormalizeThumbnailUrl(ReadString(item, "thumbnail"));
        if (list.Count == 0 && !string.IsNullOrWhiteSpace(single))
            list.Add(new Thumbnail(single, new Resolution(0, 0)));

        return list;
    }

    private static MediaSourceKind DetectSource(JObject json, string url)
    {
        var extractor = (ReadString(json, "extractor_key") ?? ReadString(json, "extractor") ?? "").ToLowerInvariant();
        if (extractor.Contains("rutube", StringComparison.Ordinal))
            return MediaSourceKind.Rutube;
        if (extractor.Contains("vk", StringComparison.Ordinal))
            return MediaSourceKind.VkVideo;
        return MediaSourceHelpers.TryGetExternalSource(url, out var kind) ? kind : MediaSourceKind.Rutube;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && value != "NA")
                return value.Trim();
        }

        return "";
    }

    private static void AppendCookies(StringBuilder args, string urlOrId, bool youtubeAccount)
    {
        if (youtubeAccount)
        {
            args.Append("--js-runtimes node ");
            args.Append("--extractor-args \"youtube:player_client=tv_downgraded\" ");
            args.Append("--cookies \"").Append(YoutubeSession.Current.CookiesFilePath).Append("\" ");
            return;
        }

        if (RutubeSession.Current.IsSignedIn && IsRutubeTarget(urlOrId) && File.Exists(RutubeSession.Current.CookiesFilePath))
            args.Append("--cookies \"").Append(RutubeSession.Current.CookiesFilePath).Append("\" ");
    }

    private static bool IsRutubeTarget(string urlOrId) =>
        urlOrId.Contains("rutube.ru", StringComparison.OrdinalIgnoreCase);

    private static string ReadString(JObject item, string name)
    {
        try
        {
            var token = item[name];
            if (token is null || token.Type is JTokenType.Null or JTokenType.Undefined)
                return "";
            if (token.Type is JTokenType.String or JTokenType.Integer or JTokenType.Float or JTokenType.Boolean)
                return token.ToString();
        }
        catch
        {
        }

        return "";
    }

    private static double? ReadDouble(JObject item, string name)
    {
        try
        {
            var token = item[name];
            if (token is null || token.Type is JTokenType.Null or JTokenType.Undefined)
                return null;
            if (token.Type is JTokenType.Integer or JTokenType.Float)
                return token.Value<double>();
            if (token.Type == JTokenType.String
                && double.TryParse(token.Value<string>(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
                return value;
        }
        catch
        {
        }

        return null;
    }

    private static long? ReadInt64(JObject item, string name)
    {
        var value = ReadDouble(item, name);
        if (value is null)
            return null;
        if (value.Value is < long.MinValue or > long.MaxValue)
            return null;
        return (long)value.Value;
    }

    private static int ReadInt32(JObject item, string name)
    {
        var value = ReadDouble(item, name);
        if (value is null)
            return 0;
        if (value.Value is < int.MinValue or > int.MaxValue)
            return 0;
        return (int)value.Value;
    }

    private static async Task<(string StdOut, string StdErr, int ExitCode)> CaptureAsync(string arguments, CancellationToken token)
    {
        using var process = CreateProcess(arguments);
        if (!process.Start())
            throw new InvalidOperationException("Failed to start yt-dlp.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
        var stderrTask = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            throw;
        }

        return (await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false), process.ExitCode);
    }

    private static async Task<int> RunAsync(string arguments, Action<string, bool> onLine, CancellationToken token)
    {
        using var process = CreateProcess(arguments);

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                onLine(e.Data, false);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                onLine(e.Data, true);
        };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start yt-dlp.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            throw;
        }

        return process.ExitCode;
    }

    private static Process CreateProcess(string arguments) => new()
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = ExePath,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        },
        EnableRaisingEvents = true,
    };

    [GeneratedRegex(@"\[download\]\s+(\d+(?:\.\d+)?)%(?:.*?at\s+(\S+))?", RegexOptions.IgnoreCase)]
    private static partial Regex PercentRegex();
}

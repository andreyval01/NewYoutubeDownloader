using System.Globalization;

namespace YoutubePlaylistDownloader.Utilities;

/// <summary>
/// Checks files already on disk before a playlist continues.
/// YouTube, Rutube and VK do not publish a checksum of the merged file, so this
/// compares the container duration with the playlist duration and treats .part files as incomplete.
/// </summary>
internal static class DownloadedFileCheck
{
    static readonly SemaphoreSlim ProbeGate = new(4, 4);
    static readonly Regex IndexName = new(@"^(?<n>0*\d+)\s*-\s", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly Regex DurationLine = new(@"Duration:\s*(?<h>\d+):(?<m>\d+):(?<s>\d+(?:\.\d+)?)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".webm", ".m4v", ".m4a", ".mp3", ".aac", ".opus", ".flac", ".wav", ".ogg"
    };

    internal readonly record struct Request(int Index, int Number, string CanonicalPath, TimeSpan? ExpectedDuration);
    internal readonly record struct Verdict(int Index, bool Complete, bool Partial, string OutputPath, string DisplayName);

    public static async Task<IReadOnlyList<Verdict>> InspectAsync(
        string directory,
        IReadOnlyList<Request> items,
        CancellationToken token)
    {
        if (items == null || items.Count == 0)
            return [];

        var files = new List<string>();
        try
        {
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                files.AddRange(Directory.EnumerateFiles(directory));
        }
        catch
        {
            files.Clear();
        }

        var byNumber = new Dictionary<int, List<string>>();
        foreach (var file in files)
        {
            var match = IndexName.Match(Path.GetFileName(file));
            if (!match.Success || !int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                continue;
            if (!byNumber.TryGetValue(number, out var group))
            {
                group = [];
                byNumber[number] = group;
            }

            group.Add(file);
        }

        var verdicts = new Verdict[items.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, items.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
            async (slot, ct) =>
            {
                var item = items[slot];
                byNumber.TryGetValue(item.Number, out var group);
                verdicts[slot] = await JudgeAsync(item, group ?? [], ct).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return verdicts;
    }

    static async Task<Verdict> JudgeAsync(Request item, List<string> group, CancellationToken token)
    {
        var parts = new List<string>();
        var finals = new List<string>();
        foreach (var path in group)
        {
            if (IsPartialName(Path.GetFileName(path)))
                parts.Add(path);
            else if (IsMedia(path))
                finals.Add(path);
        }

        foreach (var final in finals.OrderByDescending(FileLength))
        {
            token.ThrowIfCancellationRequested();
            var length = FileLength(final);
            if (length < 32 * 1024)
                continue;

            var probed = await ProbeDurationAsync(final, token).ConfigureAwait(false);
            if (probed is { } actual && DurationOk(actual, item.ExpectedDuration))
                return new Verdict(item.Index, true, false, null, Path.GetFileName(final));
            if (probed is null && length >= 5L * 1024 * 1024)
                return new Verdict(item.Index, true, false, null, Path.GetFileName(final));
            if (probed is null && item.ExpectedDuration is null && length >= 256 * 1024)
                return new Verdict(item.Index, true, false, null, Path.GetFileName(final));
        }

        if (parts.Count > 0)
        {
            var output = FinalPathFromPart(parts.OrderByDescending(FileLength).First());
            ParkBlockingFinal(finals, output);
            return new Verdict(item.Index, false, true, output, Path.GetFileName(output));
        }

        if (finals.Count > 0)
        {
            var target = finals.OrderByDescending(FileLength).First();
            ParkBlockingFinal(finals, target);
            return new Verdict(item.Index, false, true, target, Path.GetFileName(target));
        }

        var canonical = string.IsNullOrWhiteSpace(item.CanonicalPath) ? null : item.CanonicalPath;
        var display = string.IsNullOrWhiteSpace(canonical) ? null : Path.GetFileName(canonical);
        return new Verdict(item.Index, false, false, canonical, display);
    }

    static bool DurationOk(TimeSpan actual, TimeSpan? expected)
    {
        if (actual <= TimeSpan.Zero)
            return false;
        if (expected is not { } want || want.TotalSeconds < 1)
            return actual.TotalSeconds >= 1;

        var slack = Math.Max(3, want.TotalSeconds * 0.08);
        return actual.TotalSeconds + slack >= want.TotalSeconds;
    }

    static async Task<TimeSpan?> ProbeDurationAsync(string path, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(GlobalConsts.FFmpegFilePath) || !File.Exists(GlobalConsts.FFmpegFilePath))
            return null;

        await ProbeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = GlobalConsts.FFmpegFilePath,
                Arguments = "-nostdin -hide_banner -i \"" + path.Replace("\"", "") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var process = new Process { StartInfo = info };
            if (!process.Start())
                return null;

            var readError = process.StandardError.ReadToEndAsync(token);
            var readOutput = process.StandardOutput.ReadToEndAsync(token);
            var waitForExit = process.WaitForExitAsync(token);
            var finished = await Task.WhenAny(waitForExit, Task.Delay(TimeSpan.FromSeconds(20), token)).ConfigureAwait(false);
            if (finished != waitForExit || !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            var stderr = await readError.ConfigureAwait(false);
            await readOutput.ConfigureAwait(false);
            var match = DurationLine.Match(stderr ?? "");
            if (!match.Success)
                return null;

            var hours = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
            var minutes = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
            var seconds = double.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
            return TimeSpan.FromSeconds(hours * 3600 + minutes * 60 + seconds);
        }
        catch
        {
            return null;
        }
        finally
        {
            ProbeGate.Release();
        }
    }

    static void ParkBlockingFinal(List<string> finals, string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;

        foreach (var final in finals)
        {
            if (!string.Equals(final, output, StringComparison.OrdinalIgnoreCase) || !File.Exists(final))
                continue;
            try
            {
                var dest = final + ".incomplete";
                if (File.Exists(dest))
                    File.Delete(dest);
                File.Move(final, dest);
            }
            catch
            {
            }
        }
    }

    static string FinalPathFromPart(string partPath)
    {
        var directory = Path.GetDirectoryName(partPath) ?? "";
        var name = Path.GetFileName(partPath);
        var cut = name.IndexOf(".part", StringComparison.OrdinalIgnoreCase);
        if (cut > 0)
            name = name[..cut];
        return Path.Combine(directory, name);
    }

    static bool IsPartialName(string name)
        => name.Contains(".part", StringComparison.OrdinalIgnoreCase)
           || name.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase)
           || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
           || name.EndsWith(".temp", StringComparison.OrdinalIgnoreCase);

    static bool IsMedia(string path)
        => MediaExtensions.Contains(Path.GetExtension(path));

    static long FileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}

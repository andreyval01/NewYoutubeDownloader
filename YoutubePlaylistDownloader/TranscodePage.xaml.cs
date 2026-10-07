using System.Globalization;

namespace YoutubePlaylistDownloader;

public partial class TranscodePage : UserControl
{
    readonly ObservableCollection<TranscodeFileRow> files = [];
    readonly ObservableCollection<TranscodePresetRow> presets = [];
    readonly List<Process> running = [];
    readonly object runningLock = new();
    CancellationTokenSource runCts;
    bool suppressCustomEvent = true;
    bool runningJob;

    public TranscodePage()
    {
        InitializeComponent();
        FileGrid.ItemsSource = files;
        PresetGrid.ItemsSource = presets;
        FolderText.Text = GlobalConsts.settings?.SaveDirectory ?? "";
        ContainerBox.ItemsSource = new[] { "mp4", "mkv" };
        CodecBox.ItemsSource = new[] { "H.264", "HEVC" };
        HeightBox.ItemsSource = new[]
        {
            (string)FindResource("TranscodeOriginal"),
            "1080p",
            "720p",
            "480p"
        };
        AudioBox.ItemsSource = new[] { "AAC", "Opus" };
        ContainerBox.SelectedIndex = 0;
        CodecBox.SelectedIndex = 0;
        HeightBox.SelectedIndex = 0;
        AudioBox.SelectedIndex = 0;
        CrfText.Text = ((int)CrfSlider.Value).ToString();
        BitrateText.Text = ((int)BitrateSlider.Value) + " kbps";

        foreach (var preset in TranscodePlanner.BuiltIn(key => (string)FindResource(key)))
            presets.Add(new TranscodePresetRow { Preset = preset, Title = preset.Title, Note = preset.Note });
        suppressCustomEvent = false;
        if (presets.Count > 1)
            PresetGrid.SelectedIndex = 1;
        ShowEmptyEstimate();
    }

    void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "Media|*.mp4;*.mkv;*.webm;*.m4v;*.m4a;*.mp3;*.aac;*.opus;*.flac;*.wav;*.ogg|All files|*.*"
        };
        if (dialog.ShowDialog() == true)
            _ = AddPathsAsync(dialog.FileNames);
    }

    void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog();
        var current = GlobalConsts.settings?.SaveDirectory;
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            dialog.SelectedPath = current;
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;
        FolderText.Text = dialog.SelectedPath;
        var recursive = SubfoldersBox.IsChecked == true;
        _ = AddFolderAsync(dialog.SelectedPath, recursive);
    }

    async Task AddFolderAsync(string root, bool recursive)
    {
        try
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var found = await Task.Run(() => Directory.EnumerateFiles(root, "*.*", option).Where(TranscodePlanner.IsMediaFile).ToArray());
            await AddPathsAsync(found);
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => SummaryText.Text = ex.Message);
        }
    }

    async Task AddPathsAsync(IEnumerable<string> paths)
    {
        var added = new List<TranscodeFileRow>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TranscodePlanner.IsMediaFile(path) || !File.Exists(path))
                continue;
            if (new FileInfo(path).Length < 1024)
                continue;
            if (files.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var row = new TranscodeFileRow(path)
            {
                SizeText = TranscodePlanner.FormatSize(new FileInfo(path).Length),
                Status = (string)FindResource("TranscodeReading")
            };
            files.Add(row);
            added.Add(row);
        }

        RefreshEstimates();
        using var gate = new SemaphoreSlim(4);
        var tasks = added.Select(async row =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var facts = await TranscodePlanner.ProbeAsync(row.Path, CancellationToken.None).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() => ApplyFacts(row, facts));
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    row.Probing = false;
                    row.Status = (string)FindResource("TranscodeUnavailable");
                    row.EstimateText = ex.Message;
                });
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(RefreshEstimates);
    }

    void ApplyFacts(TranscodeFileRow row, MediaFacts facts)
    {
        row.Facts = facts;
        row.Probing = false;
        row.SizeText = TranscodePlanner.FormatSize(facts.Length);
        row.DurationText = facts.ProbeOk ? facts.Duration.ToString(@"hh\:mm\:ss") : "";
        row.ResolutionText = facts.HasVideo ? facts.Width + "×" + facts.Height : "";
        row.VideoCodec = facts.VideoCodec ?? "";
        row.AudioCodec = facts.AudioCodec ?? "";
        row.Status = facts.ProbeOk ? (string)FindResource("FileQueued") : (string)FindResource("TranscodeUnavailable");
        if (!facts.ProbeOk)
            row.EstimateText = (string)FindResource("TranscodeUnavailable");
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (runningJob)
            return;
        files.Clear();
        ShowEmptyEstimate();
    }

    void FileGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshEstimates();

    void PresetGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CustomPanel.Visibility = SelectedPreset()?.Id == "custom" ? Visibility.Visible : Visibility.Collapsed;
        RefreshEstimates();
    }

    void Custom_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (suppressCustomEvent)
            return;
        ApplyCustomPreset();
        RefreshEstimates();
    }

    void CrfSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CrfText != null)
            CrfText.Text = ((int)CrfSlider.Value).ToString();
        if (suppressCustomEvent)
            return;
        ApplyCustomPreset();
        RefreshEstimates();
    }

    void BitrateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BitrateText != null)
            BitrateText.Text = ((int)BitrateSlider.Value) + " kbps";
        if (suppressCustomEvent)
            return;
        ApplyCustomPreset();
        RefreshEstimates();
    }

    void ApplyCustomPreset()
    {
        var row = presets.FirstOrDefault(item => item.Preset?.Id == "custom");
        if (row == null)
            return;
        row.Preset = new TranscodePreset
        {
            Id = "custom",
            Title = row.Title,
            VideoCodec = CodecBox.SelectedIndex == 1 ? "libx265" : "libx264",
            Crf = (int)CrfSlider.Value,
            MaxHeight = HeightBox.SelectedIndex switch
            {
                1 => 1080,
                2 => 720,
                3 => 480,
                _ => 0
            },
            AudioCodec = AudioBox.SelectedIndex == 1 ? "libopus" : "aac",
            AudioBitrateKbps = (int)BitrateSlider.Value,
            Container = ContainerBox.SelectedItem as string ?? "mp4"
        };
    }

    TranscodePreset SelectedPreset()
        => (PresetGrid.SelectedItem as TranscodePresetRow)?.Preset;

    IReadOnlyList<TranscodeFileRow> Scope()
    {
        var selected = FileGrid.SelectedItems.Cast<TranscodeFileRow>().ToList();
        return selected.Count > 0 ? selected : files.ToList();
    }

    void ShowEmptyEstimate()
    {
        var hint = (string)FindResource("TranscodePickFiles");
        foreach (var row in presets)
        {
            row.CurrentText = "";
            row.EstimateText = hint;
            row.DeltaText = "";
            row.PercentText = "";
            row.BestMark = "";
        }

        SummaryText.Text = hint;
        UpdateStartButton();
    }

    void RefreshEstimates()
    {
        if (files.Count == 0)
        {
            ShowEmptyEstimate();
            return;
        }

        ApplyCustomPreset();
        var scope = Scope();
        var preset = SelectedPreset();
        var reading = (string)FindResource("TranscodeReading");
        var unavailable = (string)FindResource("TranscodeUnavailable");
        var noVideo = (string)FindResource("TranscodeNoVideo");
        var pending = scope.Any(file => file.Probing || file.Facts == null);
        long shownCurrent = 0;
        long shownEstimate = 0;
        var shownCount = 0;
        foreach (var file in files)
        {
            if (file.Probing || file.Facts == null)
            {
                file.EstimateText = reading;
                file.SavingsText = "";
                continue;
            }

            if (!scope.Contains(file))
            {
                file.EstimateText = "";
                file.SavingsText = "";
                continue;
            }

            if (preset == null || !preset.AppliesTo(file.Facts))
            {
                file.EstimateText = preset is { KeepOriginal: false, AudioOnly: false } && !file.Facts.HasVideo ? noVideo : "";
                file.SavingsText = "";
                continue;
            }

            var estimate = TranscodePlanner.EstimateBytes(file.Facts, preset);
            if (estimate is not long bytes)
            {
                file.EstimateText = unavailable;
                file.SavingsText = "";
                continue;
            }

            file.EstimateText = TranscodePlanner.FormatSize(bytes);
            file.SavingsText = TranscodePlanner.FormatPercent(file.Facts.Length, bytes);
            shownCurrent += file.Facts.Length;
            shownEstimate += bytes;
            shownCount++;
        }

        var totals = new List<(TranscodePresetRow Row, long Current, long Estimate)>();
        foreach (var row in presets)
        {
            long current = 0;
            long estimate = 0;
            var applicable = 0;
            var missing = pending;
            foreach (var file in scope)
            {
                if (file.Probing || file.Facts == null || !file.Facts.ProbeOk)
                {
                    if (row.Preset.AppliesTo(file.Facts) || file.Facts == null)
                        missing = true;
                    continue;
                }

                if (!row.Preset.AppliesTo(file.Facts))
                    continue;
                var bytes = TranscodePlanner.EstimateBytes(file.Facts, row.Preset);
                if (bytes is not long value)
                {
                    missing = true;
                    continue;
                }

                current += file.Facts.Length;
                estimate += value;
                applicable++;
            }

            if (applicable == 0)
            {
                row.CurrentText = "";
                row.EstimateText = pending
                    ? reading
                    : row.Preset.AudioOnly
                        ? ""
                        : row.Preset.KeepOriginal
                            ? unavailable
                            : noVideo;
                row.DeltaText = "";
                row.PercentText = "";
                row.BestMark = "";
                continue;
            }

            row.CurrentText = TranscodePlanner.FormatSize(current);
            row.EstimateText = TranscodePlanner.FormatSize(estimate);
            row.DeltaText = TranscodePlanner.FormatDelta(current, estimate);
            row.PercentText = TranscodePlanner.FormatPercent(current, estimate);
            row.BestMark = "";
            if (!missing)
                totals.Add((row, current, estimate));
        }

        var best = totals
            .OrderByDescending(item => item.Current - item.Estimate)
            .FirstOrDefault();
        if (best.Row != null && best.Current > best.Estimate)
            best.Row.BestMark = "★";

        if (shownCount == 0)
        {
            SummaryText.Text = pending
                ? reading
                : preset is { KeepOriginal: false, AudioOnly: false }
                    ? noVideo
                    : unavailable;
        }
        else
        {
            SummaryText.Text = string.Format(
                (string)FindResource("TranscodeSummary"),
                shownCount,
                TranscodePlanner.FormatSize(shownCurrent),
                TranscodePlanner.FormatSize(shownEstimate),
                TranscodePlanner.FormatPercent(shownCurrent, shownEstimate));
        }

        UpdateStartButton();
    }

    void UpdateStartButton()
    {
        var preset = SelectedPreset();
        var ready = !runningJob
                    && preset != null
                    && !preset.KeepOriginal
                    && Scope().Any(file => file.Facts?.ProbeOk == true && preset.AppliesTo(file.Facts) && TranscodePlanner.EstimateBytes(file.Facts, preset) is > 0);
        StartButton.IsEnabled = ready;
        CancelButton.IsEnabled = runningJob;
    }

    async void Start_Click(object sender, RoutedEventArgs e)
    {
        var preset = SelectedPreset();
        if (preset == null || preset.KeepOriginal || runningJob)
            return;

        var batch = Scope().Where(file => file.Facts?.ProbeOk == true && preset.AppliesTo(file.Facts)).ToList();
        if (batch.Count == 0)
            return;

        runningJob = true;
        UpdateStartButton();
        runCts = new CancellationTokenSource();
        var token = runCts.Token;
        var limit = DownloadSettings.NormalizeSimultaneousDownloads(GlobalConsts.DownloadSettings?.MaxSimultaneousDownloads ?? 2);
        using var gate = new SemaphoreSlim(limit);
        var force = AgainBox.IsChecked == true;
        var busy = (string)FindResource("TranscodeBusyDownload");
        var skipped = (string)FindResource("TranscodeSkipped");
        var working = (string)FindResource("TranscodeWorking");
        var done = (string)FindResource("TranscodeDone");
        var cancelled = (string)FindResource("TranscodeCancel");
        try
        {
            var tasks = batch.Select(row => EncodeAsync(row, preset, gate, force, token, busy, skipped, working, done, cancelled));
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            runningJob = false;
            UpdateStartButton();
            RefreshEstimates();
        }
    }

    async Task EncodeAsync(
        TranscodeFileRow row,
        TranscodePreset preset,
        SemaphoreSlim gate,
        bool force,
        CancellationToken token,
        string busy,
        string skipped,
        string working,
        string done,
        string cancelled)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        Process process = null;
        var temp = "";
        try
        {
            if (TranscodePlanner.IsStillDownloading(row.Path))
            {
                await SetStatus(row, busy);
                return;
            }

            if (!force && TranscodePlanner.AlreadyDone(row.Path, preset.Signature))
            {
                await SetStatus(row, skipped);
                return;
            }

            var finalPath = TranscodePlanner.OutputPath(row.Path, preset);
            temp = finalPath + ".transcode.tmp";
            if (File.Exists(temp))
                File.Delete(temp);

            await SetStatus(row, working);
            var arguments = TranscodePlanner.BuildArguments(row.Facts, preset, temp);
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GlobalConsts.FFmpegFilePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            var errors = new StringBuilder();
            process.OutputDataReceived += (_, args) => ReportProgress(row, args.Data);
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                    errors.AppendLine(args.Data);
            };
            lock (runningLock)
                running.Add(process);
            if (!process.Start())
                throw new InvalidOperationException("ffmpeg did not start.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(errors.ToString().Trim());

            TranscodePlanner.Commit(row.Path, temp, finalPath);
            TranscodePlanner.WriteMark(finalPath, preset.Signature);
            temp = "";
            await Dispatcher.InvokeAsync(() =>
            {
                row.ReplacePath(finalPath);
                row.Status = done;
                row.SavingsText = "";
            });
            var facts = await TranscodePlanner.ProbeAsync(finalPath, CancellationToken.None).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => ApplyFacts(row, facts));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await SetStatus(row, cancelled);
        }
        catch (Exception ex)
        {
            TryKill(process);
            var message = ex.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (message.Length > 180)
                message = message[..180];
            await SetStatus(row, message);
        }
        finally
        {
            if (process != null)
            {
                lock (runningLock)
                    running.Remove(process);
                process.Dispose();
            }

            if (!string.IsNullOrWhiteSpace(temp) && File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }

            gate.Release();
        }
    }

    void ReportProgress(TranscodeFileRow row, string line)
    {
        if (string.IsNullOrWhiteSpace(line) || row.Facts == null || row.Facts.Duration.TotalSeconds < 1)
            return;
        if (!line.StartsWith("out_time=", StringComparison.Ordinal))
            return;
        var text = line["out_time=".Length..];
        if (!TryOutTime(text, out var time))
            return;
        var percent = (int)Math.Clamp(time.TotalSeconds * 100 / row.Facts.Duration.TotalSeconds, 0, 99);
        _ = Dispatcher.InvokeAsync(() => row.Status = percent + "%");
    }

    async Task SetStatus(TranscodeFileRow row, string status)
    {
        await Dispatcher.InvokeAsync(() => row.Status = status);
    }

    static bool TryOutTime(string text, out TimeSpan time)
    {
        time = default;
        var parts = text.Split(':');
        if (parts.Length != 3)
            return false;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours))
            return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
            return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return false;
        time = TimeSpan.FromSeconds(hours * 3600d + minutes * 60d + seconds);
        return true;
    }

    void TryKill(Process process)
    {
        if (process == null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { runCts?.Cancel(); } catch { }
        List<Process> snapshot;
        lock (runningLock)
            snapshot = running.ToList();
        foreach (var process in snapshot)
            TryKill(process);
    }
}

using System.Globalization;

namespace YoutubePlaylistDownloader;

public partial class TranscodePage : UserControl
{
    sealed class Choice
    {
        public string Label { get; init; }
        public string Value { get; init; }
        public int Number { get; init; }
        public bool AudioOnly { get; init; }
        public override string ToString() => Label;
    }

    readonly ObservableCollection<TranscodeFileRow> files = [];
    readonly ObservableCollection<TranscodePresetRow> presets = [];
    readonly List<Process> running = [];
    readonly HashSet<Process> trimmedEncodes = [];
    readonly HashSet<string> yieldRetry = new(StringComparer.OrdinalIgnoreCase);
    bool listPositioned;
    readonly object runningLock = new();
    readonly Dictionary<TranscodeFileRow, int> fileProgress = [];
    readonly List<TranscodeFileRow> timingRows = [];
    readonly Stopwatch batchClock = new();
    DispatcherTimer clockTimer;
    bool clockTimerHooked;
    DateTime? batchFinishLocal;
    bool showFinishedMessage;
    readonly List<Choice> videoCodecs = [];
    readonly List<Choice> audioCodecs = [];
    readonly List<Choice> heights = [];
    readonly List<Choice> bitrates = [];
    static readonly string[] VideoContainerList = ["mp4", "mov", "m4v", "3gp", "avi", "mpg", "ts", "wmv", "mkv", "webm"];
    static readonly string[] AudioContainerList = ["m4a", "mp3", "ogg", "flac", "mp2", "wma", "mkv"];
    CancellationTokenSource runCts;
    System.Windows.Controls.CheckBox selectAllBox;
    string outputDirectory;
    bool suppressCustomEvent = true;
    bool suppressEncodeLimit;
    bool suppressSelectionEvent;
    bool runningJob;
    int progressTotal;

    public TranscodePage()
    {
        InitializeComponent();
        Instance = this;
        EncodeGate.LimitChanged += TrimRunningEncodes;
        suppressEncodeLimit = true;
        EncodeLimitBox.Text = DownloadSettings.NormalizeSimultaneousDownloads(GlobalConsts.DownloadSettings.MaxSimultaneousEncodes).ToString();
        suppressEncodeLimit = false;
        EncodeGate.UiChanged += ShowEncodeLimit;
        Unloaded += (_, _) => EncodeGate.UiChanged -= ShowEncodeLimit;
        FileGrid.ItemsSource = files;
        PresetGrid.ItemsSource = presets;
        FolderText.Text = GlobalConsts.settings?.SaveDirectory ?? "";
        videoCodecs.Add(new Choice { Label = (string)FindResource("TranscodeCopy"), Value = "copy" });
        videoCodecs.Add(new Choice { Label = "H.264", Value = "libx264" });
        videoCodecs.Add(new Choice { Label = "HEVC", Value = "libx265" });
        videoCodecs.Add(new Choice { Label = "VP9", Value = "libvpx-vp9" });
        videoCodecs.Add(new Choice { Label = "AV1", Value = "libaom-av1" });
        videoCodecs.Add(new Choice { Label = "MPEG-4", Value = "libxvid" });
        videoCodecs.Add(new Choice { Label = "MPEG-2", Value = "mpeg2video" });
        videoCodecs.Add(new Choice { Label = "WMV", Value = "wmv2" });
        videoCodecs.Add(new Choice { Label = (string)FindResource("TranscodeAudioOnlyOption"), Value = "none", AudioOnly = true });
        audioCodecs.Add(new Choice { Label = (string)FindResource("TranscodeCopy"), Value = "copy" });
        audioCodecs.Add(new Choice { Label = "AAC", Value = "aac" });
        audioCodecs.Add(new Choice { Label = "Opus", Value = "libopus" });
        audioCodecs.Add(new Choice { Label = "MP3", Value = "libmp3lame" });
        audioCodecs.Add(new Choice { Label = "MP2", Value = "mp2" });
        audioCodecs.Add(new Choice { Label = "WMA", Value = "wmav2" });
        audioCodecs.Add(new Choice { Label = "FLAC", Value = "flac" });
        heights.Add(new Choice { Label = (string)FindResource("TranscodeOriginal"), Number = 0 });
        foreach (var height in new[] { 2160, 1440, 1080, 720, 480, 360, 240, 144 })
            heights.Add(new Choice { Label = height + "p", Number = height });
        foreach (var rate in new[] { 64, 96, 128, 160, 192, 256, 320 })
            bitrates.Add(new Choice { Label = string.Format((string)FindResource("TranscodeKbps"), rate), Number = rate });
        CodecBox.ItemsSource = videoCodecs;
        AudioBox.ItemsSource = audioCodecs;
        HeightBox.ItemsSource = heights;
        BitrateBox.ItemsSource = bitrates;
        ContainerBox.ItemsSource = VideoContainerList;
        CrfText.Text = ((int)CrfSlider.Value).ToString();

        ReloadPresets();
        ShowEmptyEstimate();
        ShowUnfinishedJobs();
        IsVisibleChanged += (_, _) => PositionOnActiveFile(liveJob ?? ViewedJob());
    }

    void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (runningJob)
            return;
        EnsureDraft();
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "Media|*.mp4;*.mkv;*.webm;*.m4v;*.mov;*.3gp;*.3g2;*.avi;*.mpg;*.mpeg;*.ts;*.wmv;*.m4a;*.mp3;*.aac;*.opus;*.flac;*.wav;*.ogg;*.mp2;*.wma|All files|*.*"
        };
        if (dialog.ShowDialog() == true)
            _ = AddPathsAsync(dialog.FileNames);
    }

    void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (runningJob)
            return;
        EnsureDraft();
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
            var found = await Task.Run(() => Directory.EnumerateFiles(root, "*.*", option)
                .Where(TranscodePlanner.IsMediaFile)
                .Where(path => !IsTranscodeOutput(path))
                .ToArray());
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
            row.PropertyChanged += FileRow_PropertyChanged;
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
        row.AudioBitrateText = facts.AudioBitrate is > 0
            ? string.Format((string)FindResource("TranscodeKbps"), (facts.AudioBitrate.Value + 500L) / 1000L)
            : "";
        row.Status = facts.ProbeOk ? (string)FindResource("FileQueued") : (string)FindResource("TranscodeUnavailable");
        if (!facts.ProbeOk)
            row.EstimateText = (string)FindResource("TranscodeUnavailable");
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (runningJob)
            return;
        EnsureDraft();
        files.Clear();
        ClearBatchTime();
        ShowEmptyEstimate();
    }

    void OutputFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog();
        if (!string.IsNullOrWhiteSpace(outputDirectory) && Directory.Exists(outputDirectory))
            dialog.SelectedPath = outputDirectory;
        else if (!string.IsNullOrWhiteSpace(FolderText.Text) && Directory.Exists(FolderText.Text))
            dialog.SelectedPath = FolderText.Text;
        if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;
        outputDirectory = dialog.SelectedPath;
        OutputFolderText.Text = outputDirectory;
        OutputDefaultButton.Visibility = Visibility.Visible;
    }

    void OutputDefault_Click(object sender, RoutedEventArgs e)
    {
        outputDirectory = null;
        OutputFolderText.SetResourceReference(TextBlock.TextProperty, "TranscodeOutputDefault");
        OutputDefaultButton.Visibility = Visibility.Collapsed;
    }

    void SelectAll_Loaded(object sender, RoutedEventArgs e)
    {
        selectAllBox = sender as System.Windows.Controls.CheckBox;
    }

    void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox box)
            return;
        var check = box.IsChecked == true;
        suppressSelectionEvent = true;
        try
        {
            foreach (var file in files)
                file.Selected = check;
        }
        finally
        {
            suppressSelectionEvent = false;
        }

        RefreshEstimates();
    }

    void FileCheck_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox box)
            return;
        box.IsChecked = box.IsChecked != true;
        e.Handled = true;
    }

    void FileRow_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (suppressSelectionEvent || e.PropertyName != nameof(TranscodeFileRow.Selected))
            return;
        Dispatcher.InvokeAsync(RefreshEstimates);
    }

    static bool IsTranscodeOutput(string path)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        return folder.Equals("transcoding", StringComparison.OrdinalIgnoreCase);
    }

    void FileGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshEstimates();

    void PresetGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressCustomEvent)
            return;
        suppressCustomEvent = true;
        try
        {
            LoadControls(SelectedPreset());
        }
        finally
        {
            suppressCustomEvent = false;
        }

        RefreshEstimates();
        UpdatePresetButtons();
    }

    void Custom_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (suppressCustomEvent || CodecBox.SelectedItem == null)
            return;
        SyncPresetFromControls(sender == ContainerBox);
        RefreshEstimates();
    }

    void CrfSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CrfText != null)
            CrfText.Text = ((int)CrfSlider.Value).ToString();
        if (suppressCustomEvent || CodecBox?.SelectedItem == null)
            return;
        SyncPresetFromControls(false);
        RefreshEstimates();
    }

    void LoadControls(TranscodePreset preset)
    {
        if (preset == null || preset.KeepOriginal)
        {
            Select(CodecBox, videoCodecs, "copy");
            SelectNumber(HeightBox, heights, 0);
            CrfSlider.Value = 28;
            Select(AudioBox, audioCodecs, "copy");
            SelectNumber(BitrateBox, bitrates, 128);
            UseContainers(video: true, "mp4");
        }
        else if (preset.AudioOnly)
        {
            Select(CodecBox, videoCodecs, "none");
            SelectNumber(HeightBox, heights, 0);
            if (preset.Crf > 0)
                CrfSlider.Value = preset.Crf;
            Select(AudioBox, audioCodecs, preset.AudioCodec);
            SelectNumber(BitrateBox, bitrates, preset.AudioBitrateKbps > 0 ? preset.AudioBitrateKbps : 128);
            UseContainers(video: false, preset.Container);
        }
        else
        {
            Select(CodecBox, videoCodecs, string.IsNullOrWhiteSpace(preset.VideoCodec) ? "libx264" : preset.VideoCodec);
            SelectNumber(HeightBox, heights, preset.MaxHeight);
            CrfSlider.Value = preset.Crf is >= 18 and <= 35 ? preset.Crf : 28;
            Select(AudioBox, audioCodecs, string.IsNullOrWhiteSpace(preset.AudioCodec) ? "aac" : preset.AudioCodec);
            SelectNumber(BitrateBox, bitrates, preset.AudioBitrateKbps > 0 ? preset.AudioBitrateKbps : 128);
            UseContainers(video: true, preset.Container);
        }

        CoerceControls();
    }

    void SyncPresetFromControls(bool containerChosen)
    {
        suppressCustomEvent = true;
        try
        {
            CoerceControls(containerChosen);
        }
        finally
        {
            suppressCustomEvent = false;
        }

        var draft = ReadControls();
        var match = presets.FirstOrDefault(item => item.Preset != null
                                                    && item.Preset.Id != "custom"
                                                    && !item.Preset.KeepOriginal
                                                    && SameSettings(item.Preset, draft));
        suppressCustomEvent = true;
        try
        {
            if (match != null)
            {
                PresetGrid.SelectedItem = match;
                return;
            }

            var row = presets.FirstOrDefault(item => item.Preset?.Id == "custom");
            if (row == null)
                return;
            row.Preset = draft;
            row.Note = Describe(draft);
            PresetGrid.SelectedItem = row;
        }
        finally
        {
            suppressCustomEvent = false;
        }
    }

    TranscodePreset ReadControls()
    {
        var video = CodecBox.SelectedItem as Choice;
        var audio = AudioBox.SelectedItem as Choice;
        var height = HeightBox.SelectedItem as Choice;
        var bitrate = BitrateBox.SelectedItem as Choice;
        var audioOnly = video?.AudioOnly == true;
        var videoCopy = !audioOnly && TranscodePlanner.IsCopyCodec(video?.Value);
        var audioCopy = TranscodePlanner.IsCopyCodec(audio?.Value);
        var lossless = TranscodePlanner.IsLosslessAudio(audio?.Value);
        var row = presets.FirstOrDefault(item => item.Preset?.Id == "custom");
        return new TranscodePreset
        {
            Id = "custom",
            Title = row?.Title ?? "Custom",
            AudioOnly = audioOnly,
            VideoCodec = audioOnly ? null : video?.Value ?? "libx264",
            Crf = audioOnly || videoCopy ? 0 : (int)CrfSlider.Value,
            MaxHeight = audioOnly || videoCopy ? 0 : height?.Number ?? 0,
            AudioCodec = audio?.Value ?? "aac",
            AudioBitrateKbps = audioCopy || lossless ? 0 : bitrate?.Number ?? 128,
            Container = ContainerBox.SelectedItem as string ?? (audioOnly ? "m4a" : "mp4")
        };
    }

    void CoerceControls(bool containerChosen = false)
    {
        var video = CodecBox.SelectedItem as Choice;
        var audio = AudioBox.SelectedItem as Choice;
        var audioOnly = video?.AudioOnly == true;
        var current = ContainerBox.SelectedItem as string ?? (audioOnly ? "m4a" : "mp4");
        if (containerChosen)
        {
            if (audioOnly)
                ApplyAudioDefaults(current);
            else
                ApplyVideoDefaults(current);
            video = CodecBox.SelectedItem as Choice;
            audio = AudioBox.SelectedItem as Choice;
            audioOnly = video?.AudioOnly == true;
        }

        if (audioOnly)
        {
            if (!AudioUiFits(audio?.Value, current))
                current = PreferredAudio(audio?.Value);
            AssignContainers(AudioContainerList, current);
        }
        else
        {
            if (!VideoUiFits(video?.Value, audio?.Value, current))
                current = PreferredVideo(video?.Value, audio?.Value);
            AssignContainers(VideoContainerList, current);
        }

        video = CodecBox.SelectedItem as Choice;
        audio = AudioBox.SelectedItem as Choice;
        audioOnly = video?.AudioOnly == true;
        var videoCopy = !audioOnly && TranscodePlanner.IsCopyCodec(video?.Value);
        var audioCopy = TranscodePlanner.IsCopyCodec(audio?.Value);
        var lossless = TranscodePlanner.IsLosslessAudio(audio?.Value);
        HeightBox.IsEnabled = !audioOnly && !videoCopy;
        CrfSlider.IsEnabled = !audioOnly && !videoCopy;
        BitrateBox.IsEnabled = !audioCopy && !lossless;
        if (!HeightBox.IsEnabled && (HeightBox.SelectedItem as Choice)?.Number != 0)
            HeightBox.SelectedItem = heights[0];

        var container = ContainerBox.SelectedItem as string;
        if (video?.Value is "libvpx-vp9" or "libaom-av1")
        {
            CodecHint.Text = (string)FindResource("TranscodeSlowCodec");
            CodecHint.Visibility = Visibility.Visible;
        }
        else if (IsDeviceContainer(container))
        {
            CodecHint.Text = (string)FindResource("TranscodeDeviceFormat");
            CodecHint.Visibility = Visibility.Visible;
        }
        else
        {
            CodecHint.Visibility = Visibility.Collapsed;
        }
    }

    void ApplyVideoDefaults(string container)
    {
        var video = (CodecBox.SelectedItem as Choice)?.Value ?? "";
        var audio = (AudioBox.SelectedItem as Choice)?.Value ?? "";
        switch (Norm(container))
        {
            case "avi":
                if (video != "libxvid")
                    Select(CodecBox, videoCodecs, "libxvid");
                if (audio != "libmp3lame")
                    Select(AudioBox, audioCodecs, "libmp3lame");
                break;
            case "mpg":
                if (video != "mpeg2video")
                    Select(CodecBox, videoCodecs, "mpeg2video");
                if (audio != "mp2")
                    Select(AudioBox, audioCodecs, "mp2");
                break;
            case "wmv":
                if (video != "wmv2")
                    Select(CodecBox, videoCodecs, "wmv2");
                if (audio != "wmav2")
                    Select(AudioBox, audioCodecs, "wmav2");
                break;
            case "3gp":
                if (video != "libx264")
                    Select(CodecBox, videoCodecs, "libx264");
                if (audio != "aac")
                    Select(AudioBox, audioCodecs, "aac");
                break;
            case "webm":
                if (video is not ("libvpx-vp9" or "libaom-av1"))
                    Select(CodecBox, videoCodecs, "libvpx-vp9");
                if (audio != "libopus")
                    Select(AudioBox, audioCodecs, "libopus");
                break;
            case "mp4":
            case "mov":
            case "m4v":
                var videoOk = Norm(container) == "mp4"
                    ? video is "libx264" or "libx265" or "libaom-av1" or "copy"
                    : video is "libx264" or "libx265" or "copy";
                if (!videoOk)
                    Select(CodecBox, videoCodecs, "libx264");
                if (audio is not ("aac" or "libmp3lame" or "copy"))
                    Select(AudioBox, audioCodecs, "aac");
                break;
            case "ts":
                if (video is not ("libx264" or "libx265" or "mpeg2video" or "copy"))
                    Select(CodecBox, videoCodecs, "libx264");
                if (audio is not ("aac" or "libmp3lame" or "mp2" or "copy"))
                    Select(AudioBox, audioCodecs, "aac");
                break;
        }
    }

    void ApplyAudioDefaults(string container)
    {
        var codec = Norm(container) switch
        {
            "mp3" => "libmp3lame",
            "ogg" => "libopus",
            "flac" => "flac",
            "mp2" => "mp2",
            "wma" => "wmav2",
            "m4a" => "aac",
            _ => null
        };
        if (codec != null)
            Select(AudioBox, audioCodecs, codec);
    }

    void AssignContainers(string[] options, string selected)
    {
        if (!SameItems(ContainerBox.ItemsSource as IEnumerable<string>, options))
            ContainerBox.ItemsSource = options;
        var wanted = options.Contains(selected, StringComparer.OrdinalIgnoreCase) ? selected : options[0];
        if (!string.Equals(ContainerBox.SelectedItem as string, wanted, StringComparison.OrdinalIgnoreCase))
            ContainerBox.SelectedItem = wanted;
    }

    void UseContainers(bool video, string selected)
    {
        var options = video ? VideoContainerList : AudioContainerList;
        ContainerBox.ItemsSource = options;
        ContainerBox.SelectedItem = options.Contains(selected, StringComparer.OrdinalIgnoreCase) ? selected : options[0];
    }

    static bool VideoUiFits(string video, string audio, string container)
    {
        video ??= "";
        audio ??= "";
        var name = Norm(container);
        if (name == "mkv")
            return true;
        var videoOk = name switch
        {
            "mp4" => video is "libx264" or "libx265" or "libaom-av1" or "copy",
            "mov" or "m4v" => video is "libx264" or "libx265" or "copy",
            "3gp" => video == "libx264",
            "avi" => video == "libxvid",
            "mpg" => video == "mpeg2video",
            "ts" => video is "libx264" or "libx265" or "mpeg2video" or "copy",
            "wmv" => video == "wmv2",
            "webm" => video is "libvpx-vp9" or "libaom-av1",
            _ => false
        };
        if (!videoOk)
            return false;
        return name switch
        {
            "mp4" or "mov" or "m4v" => audio is "aac" or "libmp3lame" or "copy",
            "3gp" => audio == "aac",
            "avi" => audio == "libmp3lame",
            "mpg" => audio == "mp2",
            "ts" => audio is "aac" or "libmp3lame" or "mp2" or "copy",
            "wmv" => audio == "wmav2",
            "webm" => audio == "libopus",
            _ => false
        };
    }

    static string PreferredVideo(string video, string audio)
    {
        video ??= "";
        audio ??= "";
        var order = video switch
        {
            "libxvid" => new[] { "avi", "mkv" },
            "mpeg2video" => new[] { "mpg", "ts", "mkv" },
            "wmv2" => new[] { "wmv", "mkv" },
            "libvpx-vp9" => new[] { "webm", "mkv" },
            "libaom-av1" => new[] { "mp4", "webm", "mkv" },
            "libx265" => new[] { "mp4", "mov", "m4v", "ts", "mkv" },
            "libx264" => new[] { "mp4", "mov", "m4v", "3gp", "ts", "mkv" },
            "copy" => new[] { "mp4", "mov", "m4v", "ts", "mkv" },
            _ => new[] { "mkv" }
        };
        foreach (var item in order)
        {
            if (VideoUiFits(video, audio, item))
                return item;
        }

        return "mkv";
    }

    static bool AudioUiFits(string audio, string container)
    {
        var name = Norm(container);
        if (name == "mkv")
            return true;
        if (TranscodePlanner.IsCopyCodec(audio))
            return false;
        return (audio, name) switch
        {
            ("aac", "m4a") => true,
            ("libmp3lame", "mp3") => true,
            ("libopus", "ogg") => true,
            ("flac", "flac") => true,
            ("mp2", "mp2") => true,
            ("wmav2", "wma") => true,
            _ => false
        };
    }

    static string PreferredAudio(string audio)
        => audio switch
        {
            "libmp3lame" => "mp3",
            "libopus" => "ogg",
            "flac" => "flac",
            "mp2" => "mp2",
            "wmav2" => "wma",
            "aac" => "m4a",
            _ => "mkv"
        };

    static bool IsDeviceContainer(string container)
        => Norm(container) is "mov" or "m4v" or "3gp" or "avi" or "mpg" or "ts" or "wmv";

    static string Norm(string value)
        => (value ?? "").Trim().TrimStart('.').ToLowerInvariant();

    static void Select(System.Windows.Controls.ComboBox box, List<Choice> list, string value)
    {
        box.SelectedItem = list.FirstOrDefault(item => string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase)) ?? list[0];
    }

    static void SelectNumber(System.Windows.Controls.ComboBox box, List<Choice> list, int number)
    {
        var exact = list.FirstOrDefault(item => item.Number == number);
        if (exact != null)
        {
            box.SelectedItem = exact;
            return;
        }

        box.SelectedItem = list.OrderBy(item => Math.Abs(item.Number - number)).First();
    }

    static bool SameItems(IEnumerable<string> current, IReadOnlyList<string> next)
    {
        if (current == null)
            return false;
        var left = current.ToList();
        return left.Count == next.Count && left.SequenceEqual(next, StringComparer.OrdinalIgnoreCase);
    }

    static bool SameSettings(TranscodePreset left, TranscodePreset right)
        => left.AudioOnly == right.AudioOnly
           && left.Crf == right.Crf
           && left.MaxHeight == right.MaxHeight
           && left.AudioBitrateKbps == right.AudioBitrateKbps
           && string.Equals(left.VideoCodec ?? "", right.VideoCodec ?? "", StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.AudioCodec ?? "", right.AudioCodec ?? "", StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Container ?? "", right.Container ?? "", StringComparison.OrdinalIgnoreCase);

    string Describe(TranscodePreset preset)
    {
        var audio = audioCodecs.FirstOrDefault(item => string.Equals(item.Value, preset.AudioCodec, StringComparison.OrdinalIgnoreCase))?.Label
                    ?? preset.AudioCodec;
        if (preset.AudioOnly)
        {
            var rate = TranscodePlanner.IsLosslessAudio(preset.AudioCodec)
                ? (string)FindResource("TranscodeLossless")
                : TranscodePlanner.IsCopyCodec(preset.AudioCodec)
                    ? (string)FindResource("TranscodeCopy")
                    : string.Format((string)FindResource("TranscodeKbps"), preset.AudioBitrateKbps);
            return audio + " · " + rate;
        }

        var video = videoCodecs.FirstOrDefault(item => string.Equals(item.Value, preset.VideoCodec, StringComparison.OrdinalIgnoreCase))?.Label
                    ?? preset.VideoCodec;
        var height = preset.MaxHeight > 0 ? preset.MaxHeight + "p" : (string)FindResource("TranscodeOriginal");
        var audioRate = TranscodePlanner.IsCopyCodec(preset.AudioCodec) || TranscodePlanner.IsLosslessAudio(preset.AudioCodec)
            ? ""
            : " " + string.Format((string)FindResource("TranscodeKbps"), preset.AudioBitrateKbps);
        return video + " · " + height + " · " + audio + audioRate + " · " + preset.Container;
    }

    TranscodePreset SelectedPreset()
        => (PresetGrid.SelectedItem as TranscodePresetRow)?.Preset;

    IReadOnlyList<TranscodeFileRow> Scope()
        => files.Where(file => file.Selected).ToList();

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
        UpdateSelectAll();
        UpdateStartButton();
    }

    void RefreshEstimates()
    {
        if (files.Count == 0)
        {
            ShowEmptyEstimate();
            return;
        }

        UpdateSelectAll();
        var scope = Scope();
        var preset = SelectedPreset();
        if (scope.Count == 0)
        {
            foreach (var row in presets)
            {
                row.CurrentText = "";
                row.EstimateText = "";
                row.DeltaText = "";
                row.PercentText = "";
                row.BestMark = "";
            }

            foreach (var file in files)
            {
                file.EstimateText = "";
                file.SavingsText = "";
            }

            SummaryText.Text = (string)FindResource("TranscodeNoneChecked");
            UpdateStartButton();
            return;
        }

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

    void UpdateSelectAll()
    {
        if (selectAllBox == null)
            return;
        selectAllBox.IsChecked = files.Count > 0 && files.All(file => file.Selected);
    }

    void UpdateStartButton()
    {
        var preset = SelectedPreset();
        var ready = !runningJob
                    && preset != null
                    && !preset.KeepOriginal
                    && Scope().Any(file => file.Facts?.ProbeOk == true && preset.AppliesTo(file.Facts) && TranscodePlanner.EstimateBytes(file.Facts, preset) is > 0);
        var viewed = ViewedJob();
        var canContinue = !runningJob && viewed != null && CanRestart(viewed);
        StartButton.IsEnabled = ready && (viewedJobId == null || canContinue);
        PauseButton.IsEnabled = runningJob || canContinue;
        CancelButton.IsEnabled = runningJob;
        PauseButton.Content = (runningJob && batchPaused) || (!runningJob && canContinue)
            ? FindResource("TranscodeResume")
            : FindResource("TranscodePause");
        UpdateJobButtons();
    }

    static string UniqueOutput(string path, HashSet<string> used)
    {
        var full = Path.GetFullPath(path);
        if (used.Add(full))
            return full;
        var directory = Path.GetDirectoryName(full) ?? "";
        var name = Path.GetFileNameWithoutExtension(full);
        var extension = Path.GetExtension(full);
        for (var index = 2; ; index++)
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, name + " (" + index + ")" + extension));
            if (used.Add(candidate))
                return candidate;
        }
    }

    void EncodeLimitBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (suppressEncodeLimit || !int.TryParse(EncodeLimitBox.Text, out var value))
            return;
        EncodeGate.SetLimit(value, this);
    }

    void ShowEncodeLimit(int limit, object source)
    {
        if (ReferenceEquals(source, this) || EncodeLimitBox.Text == limit.ToString())
            return;
        suppressEncodeLimit = true;
        EncodeLimitBox.Text = limit.ToString();
        suppressEncodeLimit = false;
    }

    void ReportProgress(TranscodeFileRow row, string line)
    {
        if (string.IsNullOrWhiteSpace(line) || row.Facts == null || row.Facts.Duration.TotalSeconds < 1)
            return;
        line = line.Trim();
        if (!TryProgressPercent(line, row.Facts.Duration, out var percent))
            return;
        _ = Dispatcher.InvokeAsync(() =>
        {
            row.Status = percent + "%";
            row.Progress = percent;
            NoteFileProgress(row, percent, row.Name);
        });
    }

    static bool TryMediaSeconds(string line, out double seconds)
    {
        seconds = 0;
        if (line.StartsWith("out_time=", StringComparison.Ordinal))
        {
            if (!TryOutTime(line["out_time=".Length..], out var time))
                return false;
            seconds = time.TotalSeconds;
            return true;
        }

        if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
            && long.TryParse(line["out_time_us=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros))
        {
            seconds = micros / 1_000_000d;
            return true;
        }

        return false;
    }

    static bool TryProgressPercent(string line, TimeSpan duration, out int percent)
    {
        percent = 0;
        if (duration.TotalSeconds < 1 || !TryMediaSeconds(line, out var seconds))
            return false;
        percent = (int)Math.Clamp(seconds * 100 / duration.TotalSeconds, 0, 99);
        return true;
    }

    void BeginProgress(int total)
    {
        fileProgress.Clear();
        progressTotal = total;
        ProgressBar.Value = 0;
        ProgressText.Text = string.Format((string)FindResource("TranscodeProgress"), 0, 0, total);
    }

    void BeginBatchTiming(IEnumerable<TranscodeJobFile> runnable)
    {
        timingRows.Clear();
        foreach (var file in runnable)
        {
            var row = files.FirstOrDefault(item => string.Equals(item.Path, file.Source, StringComparison.OrdinalIgnoreCase));
            if (row == null)
                continue;
            row.PrepareForBatch();
            timingRows.Add(row);
        }

        batchFinishLocal = null;
        showFinishedMessage = false;
        batchClock.Restart();
        StartClockTimer();
        UpdateBatchTimeLine();
    }

    void PauseBatchClock()
    {
        if (batchClock.IsRunning)
            batchClock.Stop();
        foreach (var row in timingRows)
            row.PauseEncode();
        UpdateBatchTimeLine();
    }

    void ResumeBatchClock()
    {
        if (batchFinishLocal != null)
            return;
        if (!batchClock.IsRunning)
            batchClock.Start();
        StartClockTimer();
        UpdateBatchTimeLine();
    }

    void FinishBatchClock()
    {
        if (batchClock.IsRunning)
            batchClock.Stop();
        batchFinishLocal ??= DateTime.Now;
        clockTimer?.Stop();
        UpdateBatchTimeLine();
    }

    void ClearBatchTime()
    {
        batchClock.Reset();
        batchFinishLocal = null;
        showFinishedMessage = false;
        timingRows.Clear();
        clockTimer?.Stop();
        if (BatchTimeText != null)
            BatchTimeText.Text = "";
    }

    void StartClockTimer()
    {
        clockTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        if (!clockTimerHooked)
        {
            clockTimer.Tick += (_, _) =>
            {
                foreach (var row in timingRows)
                    row.TickEncode();
                UpdateBatchTimeLine();
            };
            clockTimerHooked = true;
        }

        if (!clockTimer.IsEnabled)
            clockTimer.Start();
    }

    void NoteEncodeStarted(TranscodeFileRow row)
    {
        if (row == null)
            return;
        void Start() => row.BeginEncode();
        if (Dispatcher.CheckAccess())
            Start();
        else
            Dispatcher.Invoke(Start);
    }

    void NoteEncodeFinished(TranscodeFileRow row)
    {
        if (row == null)
            return;
        void Finish()
        {
            row.FinishEncode();
            UpdateBatchTimeLine();
        }

        if (Dispatcher.CheckAccess())
            Finish();
        else
            Dispatcher.Invoke(Finish);
    }

    void NoteEncodeReset(TranscodeFileRow row)
    {
        if (row == null)
            return;
        void Reset() => row.ResetEncodeAttempt();
        if (Dispatcher.CheckAccess())
            Reset();
        else
            Dispatcher.Invoke(Reset);
    }

    void NoteEncodeDropped(TranscodeFileRow row)
    {
        if (row == null)
            return;
        void Drop()
        {
            row.DropEncode();
            UpdateBatchTimeLine();
        }

        if (Dispatcher.CheckAccess())
            Drop();
        else
            Dispatcher.Invoke(Drop);
    }

    void UpdateBatchTimeLine()
    {
        if (BatchTimeText == null)
            return;
        if (!batchClock.IsRunning && batchClock.Elapsed == TimeSpan.Zero && batchFinishLocal == null)
        {
            BatchTimeText.Text = "";
            return;
        }

        if (showFinishedMessage && batchFinishLocal != null && !batchClock.IsRunning)
        {
            BatchTimeText.Text = string.Format(
                CultureInfo.CurrentCulture,
                (string)FindResource("TranscodeJobFinished"),
                TranscodeFileRow.FormatEncodeClock(batchClock.Elapsed));
            return;
        }

        var elapsed = batchClock.Elapsed;
        double doneMedia = 0;
        double totalMedia = 0;
        foreach (var row in timingRows)
        {
            if (!row.CountsForEstimate)
                continue;
            var duration = row.Facts?.Duration.TotalSeconds ?? 0;
            var weight = duration >= 1 ? duration : 1;
            totalMedia += weight;
            if (row.EncodeComplete)
                doneMedia += weight;
            else if (duration >= 1)
                doneMedia += Math.Clamp(row.EncodedSeconds, 0, duration);
            else
                doneMedia += weight * Math.Clamp(row.Progress, 0, 99) / 100d;
        }

        var elapsedText = TranscodeFileRow.FormatEncodeClock(elapsed);
        string leftText;
        string totalText;
        string finishText;
        var remainingMedia = Math.Max(0, totalMedia - doneMedia);
        var known = false;
        TimeSpan remaining = TimeSpan.Zero;
        if (batchFinishLocal != null || remainingMedia < 0.5 || totalMedia <= 0)
        {
            known = true;
            remaining = TimeSpan.Zero;
        }
        else if (elapsed.TotalSeconds >= 8 && doneMedia >= 1)
        {
            var rate = doneMedia / elapsed.TotalSeconds;
            if (rate > 0)
            {
                var seconds = remainingMedia / rate;
                if (seconds < TimeSpan.FromDays(3650).TotalSeconds)
                {
                    known = true;
                    remaining = TimeSpan.FromSeconds(seconds);
                }
            }
        }

        if (!known)
        {
            var calculating = (string)FindResource("TranscodeTimeCalculating");
            leftText = calculating;
            totalText = calculating;
            finishText = calculating;
        }
        else
        {
            var exact = batchFinishLocal != null || remainingMedia < 0.5 || totalMedia <= 0;
            leftText = (exact ? "" : "~") + TranscodeFileRow.FormatEncodeClock(remaining);
            totalText = (exact ? "" : "~") + TranscodeFileRow.FormatEncodeClock(elapsed + remaining);
            var finish = batchFinishLocal ?? DateTime.Now.Add(remaining);
            finishText = finish.ToString("g", CultureInfo.CurrentCulture);
        }

        var format = batchPaused && batchFinishLocal == null
            ? (string)FindResource("TranscodeBatchTimePaused")
            : (string)FindResource("TranscodeBatchTime");
        BatchTimeText.Text = string.Format(CultureInfo.CurrentCulture, format, elapsedText, leftText, totalText, finishText);
    }

    void NoteFileProgress(TranscodeFileRow row, int percent, string detail)
    {
        if (progressTotal <= 0)
            return;
        fileProgress[row] = Math.Clamp(percent, 0, 100);
        var done = fileProgress.Values.Count(value => value >= 100);
        var overall = (int)Math.Round(fileProgress.Values.Sum() / (double)progressTotal);
        ProgressBar.Value = Math.Clamp(overall, 0, 100);
        var text = string.Format((string)FindResource("TranscodeProgress"), ProgressBar.Value, done, progressTotal);
        if (!string.IsNullOrWhiteSpace(detail) && done < progressTotal)
            text += " · " + detail;
        ProgressText.Text = text;
    }

    async Task SetStatus(TranscodeFileRow row, string status, int? progress = null)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            row.Status = status;
            if (progress is int value)
            {
                row.Progress = value;
                NoteFileProgress(row, value, row.Name);
            }
        });
    }

    static bool SameFile(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

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

}

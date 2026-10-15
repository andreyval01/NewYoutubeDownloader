using System.Globalization;

namespace YoutubePlaylistDownloader.Objects;

sealed class TranscodeFileRow : INotifyPropertyChanged
{
    bool selected = true;
    int progress;
    string durationText = "";
    string resolutionText = "";
    string videoCodec = "";
    string audioCodec = "";
    string audioBitrateText = "";
    string sizeText = "";
    string estimateText = "";
    string savingsText = "";
    string status = "";
    string encodeTimeText = "";
    Stopwatch encodeClock;
    bool encodeTicking;

    public TranscodeFileRow(string path)
    {
        Path = path ?? "";
    }

    public string Path { get; private set; }

    public void ReplacePath(string path)
    {
        Path = path ?? "";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Folder)));
    }
    public MediaFacts Facts { get; set; }
    public bool Probing { get; set; } = true;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";

    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value)
                return;
            selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    public int Progress
    {
        get => progress;
        set
        {
            var next = Math.Clamp(value, 0, 100);
            if (progress == next)
                return;
            progress = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
        }
    }

    public string DurationText { get => durationText; set => Set(ref durationText, value, nameof(DurationText)); }
    public string ResolutionText { get => resolutionText; set => Set(ref resolutionText, value, nameof(ResolutionText)); }
    public string VideoCodec { get => videoCodec; set => Set(ref videoCodec, value, nameof(VideoCodec)); }
    public string AudioCodec { get => audioCodec; set => Set(ref audioCodec, value, nameof(AudioCodec)); }
    public string AudioBitrateText { get => audioBitrateText; set => Set(ref audioBitrateText, value, nameof(AudioBitrateText)); }
    public string SizeText { get => sizeText; set => Set(ref sizeText, value, nameof(SizeText)); }
    public string EstimateText { get => estimateText; set => Set(ref estimateText, value, nameof(EstimateText)); }
    public string SavingsText { get => savingsText; set => Set(ref savingsText, value, nameof(SavingsText)); }
    public string Status { get => status; set => Set(ref status, value, nameof(Status)); }
    public string EncodeTimeText { get => encodeTimeText; set => Set(ref encodeTimeText, value, nameof(EncodeTimeText)); }
    public double EncodedSeconds { get; set; }
    public bool CountsForEstimate { get; private set; }
    public bool EncodeComplete { get; private set; }

    public void PrepareForBatch()
    {
        encodeClock = null;
        encodeTicking = false;
        EncodedSeconds = 0;
        CountsForEstimate = true;
        EncodeComplete = false;
        EncodeTimeText = "";
    }

    public void ResetEncodeAttempt()
    {
        encodeTicking = false;
        encodeClock = null;
        EncodedSeconds = 0;
        EncodeComplete = false;
        CountsForEstimate = true;
        EncodeTimeText = "";
    }

    public void BeginEncode()
    {
        encodeClock = Stopwatch.StartNew();
        encodeTicking = true;
        EncodeComplete = false;
        CountsForEstimate = true;
        EncodedSeconds = 0;
        EncodeTimeText = FormatEncodeClock(TimeSpan.Zero);
    }

    public void PauseEncode()
    {
        if (!encodeTicking)
            return;
        encodeTicking = false;
        encodeClock?.Stop();
        EncodedSeconds = 0;
        if (encodeClock != null)
            EncodeTimeText = FormatEncodeClock(encodeClock.Elapsed);
    }

    public void FinishEncode()
    {
        encodeTicking = false;
        encodeClock?.Stop();
        EncodeComplete = true;
        CountsForEstimate = true;
        var duration = Facts?.Duration.TotalSeconds ?? 0;
        EncodedSeconds = duration >= 1 ? duration : 1;
        EncodeTimeText = FormatEncodeClock(encodeClock?.Elapsed ?? TimeSpan.Zero);
    }

    public void DropEncode()
    {
        encodeTicking = false;
        encodeClock?.Stop();
        EncodeComplete = false;
        CountsForEstimate = false;
        EncodedSeconds = 0;
        if (encodeClock != null)
            EncodeTimeText = FormatEncodeClock(encodeClock.Elapsed);
    }

    public void TickEncode()
    {
        if (!encodeTicking || encodeClock == null)
            return;
        EncodeTimeText = FormatEncodeClock(encodeClock.Elapsed);
    }

    public static string FormatEncodeClock(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        var hours = (int)time.TotalHours;
        return hours >= 100
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, time.Minutes, time.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, time.Minutes, time.Seconds);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    void Set(ref string field, string value, string name)
    {
        if (field == value)
            return;
        field = value ?? "";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

sealed class TranscodePresetRow : INotifyPropertyChanged
{
    string currentText = "";
    string estimateText = "";
    string deltaText = "";
    string percentText = "";
    string bestMark = "";
    string note = "";

    public TranscodePreset Preset { get; set; }
    public string Title { get; init; }
    public string CurrentText { get => currentText; set => Set(ref currentText, value, nameof(CurrentText)); }
    public string EstimateText { get => estimateText; set => Set(ref estimateText, value, nameof(EstimateText)); }
    public string DeltaText { get => deltaText; set => Set(ref deltaText, value, nameof(DeltaText)); }
    public string PercentText { get => percentText; set => Set(ref percentText, value, nameof(PercentText)); }
    public string BestMark { get => bestMark; set => Set(ref bestMark, value, nameof(BestMark)); }
    public string Note { get => note; set => Set(ref note, value, nameof(Note)); }

    public event PropertyChangedEventHandler PropertyChanged;

    void Set(ref string field, string value, string name)
    {
        if (field == value)
            return;
        field = value ?? "";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

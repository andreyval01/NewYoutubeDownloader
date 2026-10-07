namespace YoutubePlaylistDownloader.Objects;

sealed class TranscodeFileRow : INotifyPropertyChanged
{
    string durationText = "";
    string resolutionText = "";
    string videoCodec = "";
    string audioCodec = "";
    string sizeText = "";
    string estimateText = "";
    string savingsText = "";
    string status = "";

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

    public string DurationText { get => durationText; set => Set(ref durationText, value, nameof(DurationText)); }
    public string ResolutionText { get => resolutionText; set => Set(ref resolutionText, value, nameof(ResolutionText)); }
    public string VideoCodec { get => videoCodec; set => Set(ref videoCodec, value, nameof(VideoCodec)); }
    public string AudioCodec { get => audioCodec; set => Set(ref audioCodec, value, nameof(AudioCodec)); }
    public string SizeText { get => sizeText; set => Set(ref sizeText, value, nameof(SizeText)); }
    public string EstimateText { get => estimateText; set => Set(ref estimateText, value, nameof(EstimateText)); }
    public string SavingsText { get => savingsText; set => Set(ref savingsText, value, nameof(SavingsText)); }
    public string Status { get => status; set => Set(ref status, value, nameof(Status)); }

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

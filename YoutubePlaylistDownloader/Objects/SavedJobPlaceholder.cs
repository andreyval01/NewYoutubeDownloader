namespace YoutubePlaylistDownloader.Objects;

sealed class SavedJobPlaceholder : IDownload
{
    public SavedJobPlaceholder(string jobId, string title, string status, string destination, string source, string progress, int percent, bool paused)
    {
        JobId = jobId ?? "";
        Title = string.IsNullOrWhiteSpace(title) ? source ?? "" : title;
        CurrentStatus = string.IsNullOrWhiteSpace(status) ? "" : status;
        Destination = destination ?? "";
        Source = source ?? "";
        TotalDownloaded = progress ?? "";
        CurrentProgressPercent = percent;
        IsPaused = paused;
    }

    public string JobId { get; }
    public string ImageUrl => "";
    public string Title { get; set; }
    public string TotalDownloaded { get; set; }
    public int TotalVideos { get; set; }
    public int CurrentProgressPercent { get; set; }
    public string CurrentDownloadSpeed { get; set; } = "";
    public string CurrentTitle { get; set; } = "";
    public string CurrentStatus { get; set; }
    public bool IsPaused { get; private set; }
    public string Destination { get; }
    public string Source { get; }
    public IReadOnlyList<QueueFileItem> Files { get; } = [];

    public event PropertyChangedEventHandler PropertyChanged;

    public void Pause()
    {
        IsPaused = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPaused)));
    }

    public void Resume()
    {
        IsPaused = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPaused)));
    }

    public void PauseFile(string videoId) { }
    public void ResumeFile(string videoId) { }
    public void PrioritizeFile(string videoId) { }
    public bool RetryFile(string videoId) => false;
    public void NyOpenFolder_Click(object sender, RoutedEventArgs e) { }
    public Task<bool> NyCancel() => Task.FromResult(true);
    public void Dispose() { }
}

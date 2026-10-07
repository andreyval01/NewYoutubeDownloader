namespace YoutubePlaylistDownloader.Objects;

class QueuedDownload : INotifyPropertyChanged, IDisposable
{
    public IDownload Item { get; }
    private bool disposedValue;

    public QueuedDownload(IDownload downloadItem)
    {
        Item = downloadItem;
        if (Item != null)
            Item.PropertyChanged += ItemOnPropertyChanged;
    }

    public string JobId => Item switch
    {
        DownloadPage page => page.JobId,
        SavedJobPlaceholder saved => saved.JobId,
        _ => ""
    };
    public string Title => Item?.Title;
    public string CurrentTitle => Item?.CurrentTitle;
    public string CurrentStatus => Item?.CurrentStatus;
    public string CurrentDownloadSpeed => Item?.CurrentDownloadSpeed;
    public string TotalDownloaded => Item?.TotalDownloaded;
    public int CurrentProgressPercent => Item?.CurrentProgressPercent ?? 0;
    public bool IsPaused => Item?.IsPaused ?? false;
    public string Destination => Item?.Destination;
    public string Source => Item?.Source;
    public IReadOnlyList<QueueFileItem> Files => Item?.Files ?? [];

    public event PropertyChangedEventHandler PropertyChanged;

    public void Pause() => Item?.Pause();
    public void Resume() => Item?.Resume();
    public void PauseFile(string videoId) => Item?.PauseFile(videoId);
    public void ResumeFile(string videoId) => Item?.ResumeFile(videoId);
    public void PrioritizeFile(string videoId) => Item?.PrioritizeFile(videoId);
    public bool RetryFile(string videoId) => Item?.RetryFile(videoId) ?? false;

    public Task<bool> NyCancel() => Item?.NyCancel() ?? Task.FromResult(true);

    private void ItemOnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(e.PropertyName));
        if (e.PropertyName is nameof(IDownload.CurrentTitle)
            or nameof(IDownload.CurrentStatus)
            or nameof(IDownload.CurrentDownloadSpeed)
            or nameof(IDownload.TotalDownloaded)
            or nameof(IDownload.CurrentProgressPercent)
            or nameof(IDownload.IsPaused)
            or nameof(IDownload.Title))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(e.PropertyName));
        }
    }

    public void Dispose()
    {
        if (disposedValue)
            return;
        if (Item != null)
            Item.PropertyChanged -= ItemOnPropertyChanged;
        Item?.Dispose();
        disposedValue = true;
    }
}

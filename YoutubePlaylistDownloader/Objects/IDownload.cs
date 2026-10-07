namespace YoutubePlaylistDownloader.Objects;

interface IDownload : INotifyPropertyChanged, IDisposable
{
    string ImageUrl { get; }
    string Title { get; set; }
    string TotalDownloaded { get; set; }
    int TotalVideos { get; set; }
    int CurrentProgressPercent { get; set; }
    string CurrentDownloadSpeed { get; set; }
    string CurrentTitle { get; set; }
    string CurrentStatus { get; set; }
    bool IsPaused { get; }
    string Destination { get; }
    string Source { get; }
    IReadOnlyList<QueueFileItem> Files { get; }
    void NyOpenFolder_Click(object sender, RoutedEventArgs e);
    void Pause();
    void Resume();
    void PauseFile(string videoId);
    void ResumeFile(string videoId);
    void PrioritizeFile(string videoId);
    bool RetryFile(string videoId);
    Task<bool> NyCancel();
}

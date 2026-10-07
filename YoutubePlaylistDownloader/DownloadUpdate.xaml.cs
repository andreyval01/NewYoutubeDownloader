namespace YoutubePlaylistDownloader;

/// <summary>
/// Interaction logic for DownloadUpdate.xaml
/// </summary>
public partial class DownloadUpdate : UserControl, IDownload
{
    private bool downloadFinished;

    private string title, currentTitle, currentStatus, totalDownloaded, currentDownloadSpeed;
    private int downloadPercent;
    public event PropertyChangedEventHandler PropertyChanged;

    public string ImageUrl { get; private set; }

    public string Title
    {
        get => title;
        set
        {
            title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    public string TotalDownloaded
    {
        get => totalDownloaded;
        set
        {
            totalDownloaded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalDownloaded)));
        }
    }

    public int TotalVideos
    {
        get => 1;
        set => throw new NotSupportedException($"Cannot change value of {nameof(TotalVideos)}");
    }

    public int CurrentProgressPercent
    {
        get => downloadPercent;
        set
        {
            downloadPercent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentProgressPercent)));
        }
    }

    public string CurrentDownloadSpeed
    {
        get => currentDownloadSpeed;
        set
        {
            currentDownloadSpeed = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentDownloadSpeed)));
        }
    }

    public string CurrentTitle
    {
        get => currentTitle;
        set
        {
            currentTitle = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTitle)));
        }
    }

    public string CurrentStatus
    {
        get => currentStatus;
        set
        {
            currentStatus = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentStatus)));
        }
    }
    private HttpClient httpClient;
    private readonly CancellationTokenSource cancellationTokenSource;

    public DownloadUpdate(Version latestVersion, string changelog, bool updateLater = false)
    {
        InitializeComponent();
        if (!updateLater)
        {
            GlobalConsts.NyHideSettingsButton();
            GlobalConsts.NyHideAboutButton();
            GlobalConsts.NyHideHomeButton();
            GlobalConsts.NyHideHelpButton();
        }
        ChangelogRun.Text = changelog;
        downloadFinished = false;
        GlobalConsts.UpdateSetupLocation = $"{GlobalConsts.TempFolderPath}Setup {latestVersion}.exe";

        ImageUrl = string.Empty;
        Title = $"{FindResource("DownloadingUpdateSetup")}";
        CurrentStatus = (string)FindResource("Loading");
        TotalDownloaded = $"(0/1)";
        CurrentProgressPercent = 0;
        CurrentDownloadSpeed = string.Empty;
        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue() { NoCache = true };
        cancellationTokenSource = new CancellationTokenSource();

        NyStartUpdate().ConfigureAwait(false);

        GlobalConsts.Downloads.Add(new QueuedDownload(this));
    }

    private async Task NyStartUpdate()
    {
        await Dispatcher.InvokeAsync(() => HeadlineTextBlock.Text = $"{FindResource("DownloadingUpdateSetup")}");
        await NyDownloadFileCompleted(this, new AsyncCompletedEventArgs(new InvalidOperationException((string)FindResource("CannotUpdate")), false, null));
    }

    private async void NyDownloadProgressChanged(object sender, ProgressStreamReportEventArgs args)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            CurrentDownloadProgressBar.Value += args.BytesMoved;
            CurrentDownloadProgressBarTextBlock.Text = $"{(CurrentDownloadProgressBar.Value / CurrentDownloadProgressBar.Maximum) * 100}%";
        });
    }

    private async Task NyDownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
    {
        if (e.Cancelled)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var cancelled = $"{FindResource("UpdateCancelled")}";
                HeadlineTextBlock.Text = cancelled;
                CurrentDownloadGrid.Visibility = Visibility.Collapsed;
            });
        }
        else if (e.Error != null)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var error = $"{FindResource("Error")}";
                UpdateLaterButton.Visibility = Visibility.Collapsed;
                CurrentDownloadGrid.Visibility = Visibility.Collapsed;
                HeadlineTextBlock.Text = error;
            });
            await GlobalConsts.NyShowMessage($"{FindResource($"Error")}", $"{FindResource("ErrorWhileUpdating")}");
        }
        else
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var complete = $"{FindResource("UpdateComplete")}";
                HeadlineTextBlock.Text = complete;
                CurrentDownloadGrid.Visibility = Visibility.Collapsed;
                UpdateNowButton.Visibility = Visibility.Visible;
                UpdateLaterButton.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
            });
            GlobalConsts.UpdateFinishedDownloading = true;
            downloadFinished = true;
        }
    }

    private void NyExit_Click(object sender, RoutedEventArgs e)
    {
        if (downloadFinished)
        {
            Process.Start(GlobalConsts.UpdateSetupLocation);
            Environment.Exit(0);
        }
        else
        {
            try
            {
                cancellationTokenSource.Cancel();
                httpClient.CancelPendingRequests();
                httpClient.Dispose();
                GlobalConsts.UpdateOnExit = false;
                GlobalConsts.UpdateSetupLocation = string.Empty;
                GlobalConsts.UpdateLater = false;
            }
            finally
            {
                GlobalConsts.NyLoadPage(GlobalConsts.MainPage.NyLoad());
            }
        }
    }

    private void NyExitLater_Click(object sender, RoutedEventArgs e)
    {
        GlobalConsts.UpdateOnExit = true;
        GlobalConsts.UpdateControl = this;
        GlobalConsts.UpdateLater = true;
        GlobalConsts.NyLoadPage(GlobalConsts.MainPage.NyLoad());
    }

    private async Task NyDownloadCompletedLater(object sender, AsyncCompletedEventArgs e)

    {
        await Dispatcher.InvokeAsync(async () =>
        {
            if (e.Error != null)
            {
                GlobalConsts.UpdateOnExit = false;
                GlobalConsts.UpdateSetupLocation = string.Empty;
                await GlobalConsts.NyShowMessage($"{FindResource("UpdateFailed")}", $"{string.Concat(FindResource("CannotUpdate"), e.Error.Message)}");
            }
            else if (e.Cancelled)
            {
                GlobalConsts.UpdateOnExit = false;
                GlobalConsts.UpdateSetupLocation = string.Empty;
                await GlobalConsts.NyShowMessage($"{FindResource("UpdateFailed")}", $"{string.Concat(FindResource("UpdateCancelled"), e.Error?.Message ?? "")}");
            }
            else
            {
                downloadFinished = true;
                GlobalConsts.UpdateFinishedDownloading = true;
                GlobalConsts.UpdateLater = true;
                UpdateNowButton.Visibility = Visibility.Visible;
                BackButton.Visibility = Visibility.Collapsed;
            }
        });
    }

    public DownloadUpdate NyUpdateLaterStillDownloading()
    {
        UpdateLaterButton.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;
        GlobalConsts.UpdateLater = true;
        GlobalConsts.UpdateOnExit = true;

        return this;
    }
    public bool IsPaused => false;
    public string Destination => "";
    public string Source => "";
    public IReadOnlyList<QueueFileItem> Files => [];
    public void Pause() { }
    public void Resume() { }
    public void PauseFile(string videoId) { }
    public void ResumeFile(string videoId) { }
    public void PrioritizeFile(string videoId) { }
    public bool RetryFile(string videoId) => false;
    public void NyOpenFolder_Click(object sender, RoutedEventArgs e) { }

    public void NyExit()
    {
        if (!downloadFinished)
        {
            httpClient.CancelPendingRequests();
            httpClient.Dispose();
            GlobalConsts.UpdateOnExit = false;
            GlobalConsts.UpdateSetupLocation = string.Empty;
            GlobalConsts.UpdateLater = false;
        }
        else
        {
            GlobalConsts.UpdateOnExit = true;
            GlobalConsts.UpdateControl = this;
            GlobalConsts.UpdateLater = true;
        }
    }

    public Task<bool> NyCancel()
    {
        NyExit();
        return Task.FromResult(true);
    }

    #region IDisposable Support
    private bool disposedValue = false;
    protected virtual void NyDispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {

            }
            httpClient = null;
            GlobalConsts.UpdateSetupLocation = null;
            disposedValue = true;
        }
    }
    public void Dispose()
    {
        NyDispose(true);
    }
    #endregion
}
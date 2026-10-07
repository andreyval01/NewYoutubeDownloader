using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace YoutubePlaylistDownloader;

public partial class MainPage : UserControl
{
    private readonly YoutubeClient client;
    private FullPlaylist list = null;
    private IEnumerable<IVideo> VideoList;
    private Channel channel = null;
    private readonly Dictionary<string, VideoQuality> Resolutions = new()
    {
        { "144p", YoutubeHelpers.Low144 },
        { "240p", YoutubeHelpers.Low240 },
        { "360p", YoutubeHelpers.Medium360 },
        { "480p", YoutubeHelpers.Medium480 },
        { "720p", YoutubeHelpers.High720 },
        { "1080p", YoutubeHelpers.High1080 },
        { "1440p", YoutubeHelpers.High1440 },
        { "2160p", YoutubeHelpers.High2160 },
        { "2880p", YoutubeHelpers.High2880 },
        { "3072p", YoutubeHelpers.High3072 },
        { "4320p", YoutubeHelpers.High4320 }
    };
    private readonly string[] VideoFileTypes = ["mp4", "mkv"];

    private readonly string[] FileTypes = ["mp3", "aac", "opus", "wav", "flac", "m4a", "ogg", "webm"];
    private CancellationTokenSource lookupCts;
    private int playlistImageGeneration;
    private string currentSourceUrl;
    private List<RelatedPlaylistItem> catalogPlaylists;
    private ICollectionView queueView;
    private QueuedDownload filesOwner;
    private readonly DispatcherTimer queueStatusTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainPage()
    {
        InitializeComponent();
        DataObject.AddPastingHandler(BulkLinksTextBox, NyBulkLinksTextBox_OnPaste);
        GlobalConsts.NyHideHomeButton();
        GlobalConsts.NyShowSettingsButton();
        GlobalConsts.NyShowAboutButton();
        GlobalConsts.NyShowHelpButton();
        VideoList = new List<IVideo>();
        client = GlobalConsts.YoutubeClient;

        GlobalConsts.MainPage = this;
        queueView = CollectionViewSource.GetDefaultView(GlobalConsts.Downloads);
        queueView.Filter = FilterQueue;
        QueueGrid.ItemsSource = queueView;
        queueStatusTimer.Tick += (_, _) => UpdateQueueStatus();
        queueStatusTimer.Start();
        QueueFilesHint.Visibility = Visibility.Visible;
        UpdateQueueStatus();
    }

    public MainPage NyLoad()
    {
        GlobalConsts.NyHideHomeButton();
        GlobalConsts.NyShowSettingsButton();
        GlobalConsts.NyShowAboutButton();
        GlobalConsts.NyShowHelpButton();
        return this;
    }

    private async void NyTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var url = PlaylistLinkTextBox.Text;
        currentSourceUrl = url;
        catalogPlaylists = null;
        try
        {
            lookupCts?.Cancel();
            lookupCts = new CancellationTokenSource();
            var lookupToken = lookupCts.Token;

            if (YoutubeHelpers.NyTryParsePlaylistId(url, out var playlistId))
            {
                _ = Task.Run(async () =>
                {
                    var basePlaylist = await client.Playlists.GetAsync(playlistId.Value).ConfigureAwait(false);
                    list = new FullPlaylist(basePlaylist, await client.Playlists.GetVideosAsync(basePlaylist.Id).CollectAsync().ConfigureAwait(false));
                    VideoList = new List<PlaylistVideo>();
                    await NyUpdatePlaylistInfo(Visibility.Visible, list.BasePlaylist.Title, list.BasePlaylist.Author?.ChannelTitle ?? "", "", list.Videos.Count().ToString(), $"https://img.youtube.com/vi/{list?.Videos?.FirstOrDefault()?.Id}/maxresdefault.jpg", true, true);
                });
            }
            else if (YoutubeHelpers.NyTryParseChannelId(url, out var channelId))
            {
                _ = Task.Run(async () =>
                {
                    channel = await client.Channels.GetAsync(channelId).ConfigureAwait(false);
                    list = new FullPlaylist(null, null, channel.Title);
                    VideoList = await client.Channels.GetUploadsAsync(channel.Id).CollectAsync().ConfigureAwait(false);
                    await NyUpdatePlaylistInfo(Visibility.Visible, channel.Title, totalVideos: VideoList.Count().ToString(), imageUrl: channel.Thumbnails.FirstOrDefault()?.Url, downloadEnabled: true, showIndexes: true);
                });
            }
            else if (YoutubeHelpers.NyTryParseUsername(url, out var username))
            {
                _ = Task.Run(async () =>
                {
                    var channel = await client.Channels.GetByUserAsync(username).ConfigureAwait(false);
                    list = new FullPlaylist(null, null, channel.Title);
                    VideoList = await client.Channels.GetUploadsAsync(channel.Id).CollectAsync().ConfigureAwait(false);
                    await NyUpdatePlaylistInfo(Visibility.Visible, channel.Title, totalVideos: VideoList.Count().ToString(), imageUrl: channel.Thumbnails.FirstOrDefault()?.Url, downloadEnabled: true, showIndexes: true);
                });
            }
            else if (YoutubeHelpers.NyTryParseHandle(url, out var handle))
            {
                _ = Task.Run(async () =>
                {
                    var channel = await client.Channels.GetByHandleAsync(handle).ConfigureAwait(false);
                    list = new FullPlaylist(null, null, channel.Title);
                    VideoList = await client.Channels.GetUploadsAsync(channel.Id).CollectAsync().ConfigureAwait(false);
                    await NyUpdatePlaylistInfo(Visibility.Visible, channel.Title, totalVideos: VideoList.Count().ToString(), imageUrl: channel.Thumbnails.FirstOrDefault()?.Url, downloadEnabled: true, showIndexes: true);
                });
            }
            else if (YoutubeHelpers.NyTryParseVideoId(url, out var videoId))
            {
                _ = Task.Run(async () =>
                {
                    var video = await client.Videos.GetAsync(videoId);
                    VideoList = new List<Video> { video };
                    list = new FullPlaylist(null, null);
                    await NyUpdatePlaylistInfo(Visibility.Visible, video.Title, video.Author.ChannelTitle, video.Engagement.ViewCount.ToString(), string.Empty, $"https://img.youtube.com/vi/{video.Id}/maxresdefault.jpg", true, false);
                });
            }
            else if (MediaSourceHelpers.IsPlaylistCatalog(url) || MediaSourceHelpers.TryGetExternalSource(url, out _))
            {
                if (MediaSourceHelpers.IsPlaylistCatalog(url))
                {
                    await LoadPlaylistCatalogAsync(url, lookupToken);
                    return;
                }

                if (!YtDlpDownloader.IsAvailable)
                {
                    var missingTitle = (string)FindResource("Error");
                    var missingBody = string.Format((string)FindResource("FileDoesNotExist"), "yt-dlp.exe");
                    await NyUpdatePlaylistInfo();
                    await GlobalConsts.NyShowMessage(missingTitle, missingBody);
                    return;
                }

                var loadingTitle = (string)FindResource("Loading");
                await NyUpdatePlaylistInfo(Visibility.Visible, loadingTitle, downloadEnabled: false);
                var info = await YtDlpDownloader.GetInfoAsync(url, lookupToken);
                lookupToken.ThrowIfCancellationRequested();
                VideoList = info.Videos;
                list = new FullPlaylist(null, info.Videos, info.Title, info.IsPlaylist);
                await NyUpdatePlaylistInfo(
                    Visibility.Visible,
                    info.Title,
                    info.Uploader,
                    info.IsPlaylist ? "" : info.ViewCount?.ToString() ?? "",
                    info.IsPlaylist ? info.Videos.Count.ToString() : string.Empty,
                    info.Thumbnail,
                    info.Videos.Count > 0,
                    info.IsPlaylist
                );
            }
            else
            {
                await NyUpdatePlaylistInfo();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            await GlobalConsts.NyLog(ex.ToString(), "MainPage TextBox_TextChanged");
            await NyUpdatePlaylistInfo();
            var title = await Dispatcher.InvokeAsync(() => (string)FindResource("Error"));
            await GlobalConsts.NyShowMessage(title, ex.Message);
        }
    }

    private void NyDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryStartCatalogDownload())
            return;

        if (list != null || VideoList.Any())
        {
            if (!NyCanDownload())
            {
                GlobalConsts.NyShowMessage((string)FindResource("Error"), $"{string.Format((string)FindResource("FileDoesNotExist"), MissingDownloadTool())}").ConfigureAwait(false);
                return;
            }

            GlobalConsts.NyLoadPage(new DownloadPage(list, GlobalConsts.DownloadSettings.NyClone(), videos: VideoList, sourceUrl: currentSourceUrl));
            VideoList = new List<IVideo>();
            PlaylistLinkTextBox.Text = string.Empty;
        }
    }

    private async Task NyUpdatePlaylistInfo(Visibility vis = Visibility.Collapsed, string title = "", string author = "", string views = "", string totalVideos = "", string imageUrl = "", bool downloadEnabled = false, bool showIndexes = false)
        => await Dispatcher.InvokeAsync(() =>
        {
            SetPlaylistImage(imageUrl);

            PlaylistInfoGrid.Visibility = vis;
            PlaylistTitleTextBlock.Text = title;
            PlaylistAuthorTextBlock.Text = author;
            PlaylistViewsTextBlock.Text = views;

            if (!string.IsNullOrWhiteSpace(totalVideos))
            {
                PlaylistTotalVideosTextBlockText.Visibility = Visibility.Visible;
                PlaylistTotalVideosTextBlock.Visibility = Visibility.Visible;
                PlaylistTotalVideosTextBlock.Text = totalVideos;
            }
            else
            {
                PlaylistTotalVideosTextBlockText.Visibility = Visibility.Collapsed;
                PlaylistTotalVideosTextBlock.Visibility = Visibility.Collapsed;
            }

            DownloadButton.IsEnabled = downloadEnabled;
            DownloadInBackgroundButton.IsEnabled = downloadEnabled;

            RelatedPlaylistsPanel.Visibility = vis;
            if (vis != Visibility.Visible)
            {
                RelatedPlaylistsList.ItemsSource = null;
                AddRelatedPlaylistsButton.IsEnabled = false;
                RelatedPlaylistsStatus.Text = "";
            }
        });

    private void SetPlaylistImage(string imageUrl)
    {
        var generation = Interlocked.Increment(ref playlistImageGeneration);
        PlaylistInfoImage.Source = null;
        if (string.IsNullOrWhiteSpace(imageUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri)
            || (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps))
        {
            PlaylistInfoImage.Visibility = Visibility.Collapsed;
            return;
        }

        PlaylistInfoImage.Visibility = Visibility.Collapsed;
        _ = LoadPlaylistImageAsync(imageUri, generation);
    }

    private async Task LoadPlaylistImageAsync(Uri imageUri, int generation)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            var bytes = await client.GetByteArrayAsync(imageUri).ConfigureAwait(false);
            if (generation != playlistImageGeneration || bytes is not { Length: > 0 })
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (generation != playlistImageGeneration)
                    return;
                try
                {
                    var bitmap = new BitmapImage();
                    using var stream = new MemoryStream(bytes);
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    PlaylistInfoImage.Source = bitmap;
                    PlaylistInfoImage.Visibility = Visibility.Visible;
                }
                catch
                {
                    PlaylistInfoImage.Source = null;
                    PlaylistInfoImage.Visibility = Visibility.Collapsed;
                }
            });
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (generation != playlistImageGeneration)
                    return;
                PlaylistInfoImage.Source = null;
                PlaylistInfoImage.Visibility = Visibility.Collapsed;
            });
        }
    }

    private void NyDownloadInBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        if (TryStartCatalogDownload())
            return;

        if (list != null || VideoList.Any())
        {
            if (!NyCanDownload())
            {
                GlobalConsts.NyShowMessage((string)FindResource("Error"), $"{string.Format((string)FindResource("FileDoesNotExist"), MissingDownloadTool())}").ConfigureAwait(false);
                return;
            }

            _ = new DownloadPage(list, GlobalConsts.DownloadSettings.NyClone(), silent: true, videos: VideoList, sourceUrl: currentSourceUrl);
            VideoList = new List<IVideo>();
            PlaylistLinkTextBox.Text = string.Empty;
        }
    }

    private void NyTile_Click(object sender, RoutedEventArgs e)
    {
        GlobalConsts.NyLoadFlyoutPage(new DownloadSettingsControl());
    }

    private void NyBulkDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var links = BulkLinksTextBox.Text.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        if (!NyCanDownload())
        {
            GlobalConsts.NyShowMessage((string)FindResource("Error"), $"{string.Format((string)FindResource("FileDoesNotExist"), MissingDownloadTool())}").ConfigureAwait(false);
            return;
        }

        _ = DownloadPage.NySequenceDownload(links, GlobalConsts.DownloadSettings.NyClone(), silent: true);
        BulkLinksTextBox.Text = string.Empty;
        MetroAnimatedTabControl.SelectedItem = QueueMetroTabItem;
    }

    public void NyChangeToQueueTab()
    {
        MetroAnimatedTabControl.SelectedItem = QueueMetroTabItem;
    }

    public void ChangeToBulkTab()
    {
        MetroAnimatedTabControl.SelectedItem = BulkMetroTabItem;
    }

    private async void FindRelatedPlaylists_Click(object sender, RoutedEventArgs e)
    {
        var url = currentSourceUrl;
        if (string.IsNullOrWhiteSpace(url))
            url = PlaylistLinkTextBox.Text;
        if (string.IsNullOrWhiteSpace(url))
            return;

        var loading = (string)FindResource("Loading");
        var none = (string)FindResource("RelatedPlaylistsNone");
        var foundFormat = (string)FindResource("RelatedPlaylistsFound");
        var errorTitle = (string)FindResource("Error");
        FindRelatedPlaylistsButton.IsEnabled = false;
        AddRelatedPlaylistsButton.IsEnabled = false;
        RelatedPlaylistsStatus.Text = loading;
        RelatedPlaylistsList.ItemsSource = null;

        try
        {
            var token = lookupCts?.Token ?? CancellationToken.None;
            var found = await PlaylistLinkScanner.FindAsync(url, token);
            var items = found.Select(x => new RelatedPlaylistItem(x.Title, x.Url)).ToList();
            RelatedPlaylistsList.ItemsSource = items;
            AddRelatedPlaylistsButton.IsEnabled = items.Count > 0;
            RelatedPlaylistsStatus.Text = items.Count == 0
                ? none
                : string.Format(foundFormat, items.Count);
            if (items.Count > 0)
                RelatedPlaylistsList.SelectAll();
        }
        catch (OperationCanceledException)
        {
            RelatedPlaylistsStatus.Text = "";
        }
        catch (Exception ex)
        {
            await GlobalConsts.NyLog(ex.ToString(), "MainPage FindRelatedPlaylists");
            RelatedPlaylistsStatus.Text = "";
            await GlobalConsts.NyShowMessage(errorTitle, ex.Message);
        }
        finally
        {
            FindRelatedPlaylistsButton.IsEnabled = true;
        }
    }

    private void AddRelatedPlaylists_Click(object sender, RoutedEventArgs e)
    {
        var selected = RelatedPlaylistsList.SelectedItems.OfType<RelatedPlaylistItem>().ToList();
        if (selected.Count == 0)
            selected = RelatedPlaylistsList.Items.OfType<RelatedPlaylistItem>().ToList();
        if (selected.Count == 0)
            return;

        var existing = new HashSet<string>(
            BulkLinksTextBox.Text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var builder = new StringBuilder(BulkLinksTextBox.Text);
        if (builder.Length > 0 && !BulkLinksTextBox.Text.EndsWith('\n') && !BulkLinksTextBox.Text.EndsWith('\r'))
            builder.AppendLine();

        foreach (var item in selected)
        {
            if (string.IsNullOrWhiteSpace(item.Url) || !existing.Add(item.Url))
                continue;
            builder.AppendLine(item.Url);
            added++;
        }

        if (added == 0)
            return;

        BulkLinksTextBox.Text = builder.ToString();
        BulkLinksTextBox.SelectionStart = BulkLinksTextBox.Text.Length;
        RelatedPlaylistsStatus.Text = string.Format((string)FindResource("RelatedPlaylistsAdded"), added);
        ChangeToBulkTab();
    }

    private void NyTextBox_TextChanged_1(object sender, TextChangedEventArgs e)
    {
        BulkDownloadButton.IsEnabled = !string.IsNullOrWhiteSpace(BulkLinksTextBox.Text);
    }

    private bool NyCanDownload()
    {
        if (catalogPlaylists is { Count: > 0 })
            return YtDlpDownloader.IsAvailable;
        if (!GlobalConsts.DownloadSettings.AudioOnly && !File.Exists(GlobalConsts.FFmpegFilePath))
            return false;
        if (VideoList != null && VideoList.OfType<GenericVideo>().Any() && !YtDlpDownloader.IsAvailable)
            return false;
        return true;
    }

    private async Task LoadPlaylistCatalogAsync(string url, CancellationToken token)
    {
        var loadingTitle = (string)FindResource("Loading");
        var none = (string)FindResource("RelatedPlaylistsNone");
        var foundFormat = (string)FindResource("RelatedPlaylistsFound");
        var catalogTitle = (string)FindResource("ChannelPlaylists");
        await NyUpdatePlaylistInfo(Visibility.Visible, loadingTitle, downloadEnabled: false);
        var scan = await PlaylistLinkScanner.ScanAsync(url, token);
        token.ThrowIfCancellationRequested();
        var items = scan.Playlists.Select(x => new RelatedPlaylistItem(x.Title, x.Url)).ToList();
        catalogPlaylists = items;
        VideoList = new List<IVideo>();
        list = null;
        var title = string.IsNullOrWhiteSpace(scan.PageTitle) ? catalogTitle : scan.PageTitle;
        await NyUpdatePlaylistInfo(
            Visibility.Visible,
            title,
            catalogTitle,
            "",
            items.Count.ToString(),
            scan.Thumbnail,
            items.Count > 0,
            true);
        await Dispatcher.InvokeAsync(() =>
        {
            RelatedPlaylistsList.ItemsSource = items;
            AddRelatedPlaylistsButton.IsEnabled = items.Count > 0;
            RelatedPlaylistsStatus.Text = items.Count == 0 ? none : string.Format(foundFormat, items.Count);
            if (items.Count > 0)
                RelatedPlaylistsList.SelectAll();
        });
    }

    private bool TryStartCatalogDownload()
    {
        if (catalogPlaylists is not { Count: > 0 })
            return false;

        if (!NyCanDownload())
        {
            GlobalConsts.NyShowMessage((string)FindResource("Error"), $"{string.Format((string)FindResource("FileDoesNotExist"), MissingDownloadTool())}").ConfigureAwait(false);
            return true;
        }

        var selected = RelatedPlaylistsList.SelectedItems.OfType<RelatedPlaylistItem>().ToList();
        if (selected.Count == 0)
            selected = catalogPlaylists;

        _ = DownloadPage.NySequenceDownload(selected.Select(item => item.Url), GlobalConsts.DownloadSettings.NyClone(), silent: true);
        catalogPlaylists = null;
        VideoList = new List<IVideo>();
        PlaylistLinkTextBox.Text = string.Empty;
        NyChangeToQueueTab();
        return true;
    }

    private string MissingDownloadTool()
    {
        if (catalogPlaylists is { Count: > 0 } || (VideoList != null && VideoList.OfType<GenericVideo>().Any()))
            return "yt-dlp.exe";
        return GlobalConsts.FFmpegFilePath;
    }

    private void NyBulkLinksTextBox_PreviewDrop(object sender, DragEventArgs e)
    {
        var data = e.Data.GetData(DataFormats.Text, true);
        if (data != null)
        {
            var dataAsString = (string)data;
            dataAsString += Environment.NewLine;
            BulkLinksTextBox.Text += dataAsString;
            BulkLinksTextBox.SelectionStart = BulkLinksTextBox.Text.Length;
            e.Handled = true;
        }
    }

    private List<QueuedDownload> SelectedJobs()
    {
        return QueueGrid.SelectedItems.OfType<QueuedDownload>().Distinct().ToList();
    }

    private QueuedDownload FocusedJob()
    {
        if (QueueGrid.CurrentItem is QueuedDownload current && QueueGrid.SelectedItems.Contains(current))
            return current;
        return QueueGrid.SelectedItem as QueuedDownload;
    }

    private List<QueueFileItem> SelectedFiles()
    {
        return QueueFilesList.SelectedItems.OfType<QueueFileItem>().Distinct().OrderBy(file => file.Number).ToList();
    }

    private void RememberJobs(IReadOnlyList<QueuedDownload> jobs)
    {
        QueueGrid.SelectedItems.Clear();
        foreach (var job in jobs)
        {
            if (GlobalConsts.Downloads.Contains(job))
                QueueGrid.SelectedItems.Add(job);
        }
    }

    private void QueuePause_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in SelectedJobs())
            job.Pause();
    }

    private void QueueResume_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in SelectedJobs())
            job.Resume();
    }

    private void QueueUp_Click(object sender, RoutedEventArgs e)
    {
        var jobs = SelectedJobs();
        GlobalConsts.MoveDownloads(jobs, -1);
        RememberJobs(jobs);
    }

    private void QueueDown_Click(object sender, RoutedEventArgs e)
    {
        var jobs = SelectedJobs();
        GlobalConsts.MoveDownloads(jobs, 1);
        RememberJobs(jobs);
    }

    private void QueueTop_Click(object sender, RoutedEventArgs e)
    {
        var jobs = SelectedJobs();
        GlobalConsts.MoveDownloadsToEdge(jobs, toTop: true);
        RememberJobs(jobs);
    }

    private void QueueBottom_Click(object sender, RoutedEventArgs e)
    {
        var jobs = SelectedJobs();
        GlobalConsts.MoveDownloadsToEdge(jobs, toTop: false);
        RememberJobs(jobs);
    }

    private async void QueueCancel_Click(object sender, RoutedEventArgs e)
    {
        var jobs = SelectedJobs();
        if (jobs.Count == 0)
            return;
        if (jobs.Count > 1)
        {
            var answer = await GlobalConsts.NyShowYesNoDialog(
                (string)FindResource("Cancel"),
                string.Format((string)FindResource("CancelSelectedConfirm"), jobs.Count));
            if (answer != MessageDialogResult.Affirmative)
                return;
        }

        foreach (var job in jobs)
        {
            if (await job.NyCancel())
                GlobalConsts.Downloads.Remove(job);
        }

        UpdateQueueStatus();
    }

    private void QueuePauseFile_Click(object sender, RoutedEventArgs e)
    {
        var job = filesOwner;
        if (job == null)
            return;
        foreach (var file in SelectedFiles())
            job.PauseFile(file.Id);
    }

    private void QueueResumeFile_Click(object sender, RoutedEventArgs e)
    {
        var job = filesOwner;
        if (job == null)
            return;
        foreach (var file in SelectedFiles())
            job.ResumeFile(file.Id);
    }

    private void QueuePrioritizeFile_Click(object sender, RoutedEventArgs e)
    {
        var job = filesOwner;
        if (job == null)
            return;
        var files = SelectedFiles();
        for (var i = files.Count - 1; i >= 0; i--)
            job.PrioritizeFile(files[i].Id);
    }

    private async void QueueRetryFile_Click(object sender, RoutedEventArgs e)
    {
        var job = filesOwner;
        if (job == null)
            return;
        var retried = false;
        foreach (var file in SelectedFiles())
        {
            if (IsFileStatus(file, "FileError"))
                retried |= job.RetryFile(file.Id);
        }

        if (!retried)
            await GlobalConsts.NyShowMessage((string)FindResource("RetryFile"), (string)FindResource("QueueRetryUnavailable"));
    }

    private void QueueOpenFolder_Click(object sender, RoutedEventArgs e) => OpenJobFolder(FocusedJob());

    private void QueueCopyLink_Click(object sender, RoutedEventArgs e) => CopyText(FocusedJob()?.Source);

    private void QueueCopyPath_Click(object sender, RoutedEventArgs e) => CopyText(FocusedJob()?.Destination);

    private void QueueSelectAll_Click(object sender, RoutedEventArgs e) => QueueGrid.SelectAll();

    private void QueueClearSelection_Click(object sender, RoutedEventArgs e) => QueueGrid.UnselectAll();

    private void QueueOpenFile_Click(object sender, RoutedEventArgs e)
    {
        var job = filesOwner;
        foreach (var file in SelectedFiles())
        {
            var path = FilePathOf(job, file);
            if (File.Exists(path))
                OpenPath(path);
        }
    }

    private void QueueOpenFileFolder_Click(object sender, RoutedEventArgs e) => OpenJobFolder(filesOwner);

    private void QueueCopyFileName_Click(object sender, RoutedEventArgs e)
    {
        var paths = SelectedFiles().Select(file => FilePathOf(filesOwner, file)).Where(path => !string.IsNullOrWhiteSpace(path));
        CopyText(string.Join(Environment.NewLine, paths));
    }

    private void QueueSelectAllFiles_Click(object sender, RoutedEventArgs e) => QueueFilesList.SelectAll();

    private void QueueSelectErrorFiles_Click(object sender, RoutedEventArgs e) => SelectFilesByStatus("FileError");

    private void QueueSelectPausedFiles_Click(object sender, RoutedEventArgs e) => SelectFilesByStatus("FilePaused");

    private void QueueClearFileSelection_Click(object sender, RoutedEventArgs e) => QueueFilesList.UnselectAll();

    private void QueueFilter_TextChanged(object sender, TextChangedEventArgs e) => queueView?.Refresh();

    private bool FilterQueue(object item)
    {
        if (item is not QueuedDownload job)
            return false;
        var text = QueueFilterBox?.Text;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        return Contains(job.Title, text) || Contains(job.CurrentTitle, text) || Contains(job.Destination, text) || Contains(job.Source, text);
    }

    private static bool Contains(string value, string text) =>
        !string.IsNullOrEmpty(value) && value.Contains(text, StringComparison.OrdinalIgnoreCase);

    private void QueueGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = SelectedJobs();
        var focused = FocusedJob();
        if (selected.Count > 1)
        {
            QueueSummaryText.Visibility = Visibility.Visible;
            QueueSingleInfo.Visibility = Visibility.Collapsed;
            var downloading = (string)FindResource("Downloading");
            var paused = selected.Count(job => job.IsPaused);
            var active = selected.Count(job => !job.IsPaused && (job.CurrentStatus == downloading || ParseSpeed(job.CurrentDownloadSpeed) > 0));
            QueueSummaryText.Text = string.Format((string)FindResource("QueueSummary"), selected.Count, paused, active);
        }
        else
        {
            QueueSummaryText.Visibility = Visibility.Collapsed;
            QueueSingleInfo.Visibility = Visibility.Visible;
            QueueSingleInfo.DataContext = focused;
        }

        if (!ReferenceEquals(filesOwner, focused))
        {
            filesOwner = focused;
            QueueFilesList.ItemsSource = focused?.Files;
        }

        QueueFilesHint.Visibility = focused == null ? Visibility.Visible : Visibility.Collapsed;
        UpdateQueueStatus();
    }

    private void QueueFiles_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateQueueStatus();

    private void QueueGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not QueuedDownload job)
            return;
        if (!QueueGrid.SelectedItems.Contains(job))
        {
            QueueGrid.SelectedItems.Clear();
            QueueGrid.SelectedItem = job;
        }
    }

    private void QueueFiles_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindParent<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject);
        if (row?.Content is not QueueFileItem file)
            return;
        if (!QueueFilesList.SelectedItems.Contains(file))
        {
            QueueFilesList.SelectedItems.Clear();
            QueueFilesList.SelectedItem = file;
        }
    }

    private void QueueGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            QueueGrid.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            QueueCancel_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            ToggleJobs();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
        {
            QueueUp_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
        {
            QueueDown_Click(sender, e);
            e.Handled = true;
        }
    }

    private void QueueFiles_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            QueueFilesList.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            ToggleFiles();
            e.Handled = true;
        }
    }

    private void QueueGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject) == null)
            return;
        OpenJobFolder(FocusedJob());
    }

    private void QueueFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<System.Windows.Controls.ListViewItem>(e.OriginalSource as DependencyObject) == null)
            return;
        var file = QueueFilesList.SelectedItem as QueueFileItem;
        var path = FilePathOf(filesOwner, file);
        if (IsFileStatus(file, "FileDone") && File.Exists(path))
            OpenPath(path);
        else
            OpenJobFolder(filesOwner);
    }

    private void QueueJobMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;
        var job = FocusedJob();
        var hasPath = !string.IsNullOrWhiteSpace(job?.Destination);
        SetMenuEnabled(menu, "OpenFolder", hasPath);
        SetMenuEnabled(menu, "CopyLink", !string.IsNullOrWhiteSpace(job?.Source));
        SetMenuEnabled(menu, "CopyPath", hasPath);
    }

    private void QueueFileMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;
        var files = SelectedFiles();
        var job = filesOwner;
        SetMenuEnabled(menu, "RetryFile", files.Any(file => IsFileStatus(file, "FileError")));
        SetMenuEnabled(menu, "OpenFile", files.Any(file => File.Exists(FilePathOf(job, file))));
        SetMenuEnabled(menu, "OpenFolder", !string.IsNullOrWhiteSpace(job?.Destination));
        SetMenuEnabled(menu, "CopyFileName", files.Count > 0);
    }

    private static void SetMenuEnabled(ContextMenu menu, string tag, bool enabled)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if ((item.Tag as string) == tag)
                item.IsEnabled = enabled;
        }
    }

    private void ToggleJobs()
    {
        var jobs = SelectedJobs();
        if (jobs.Count == 0)
            return;
        if (jobs.All(job => job.IsPaused))
        {
            foreach (var job in jobs)
                job.Resume();
        }
        else
        {
            foreach (var job in jobs)
                job.Pause();
        }
    }

    private void ToggleFiles()
    {
        var job = filesOwner;
        var files = SelectedFiles();
        if (job == null || files.Count == 0)
            return;
        if (files.All(file => IsFileStatus(file, "FilePaused")))
        {
            foreach (var file in files)
                job.ResumeFile(file.Id);
        }
        else
        {
            foreach (var file in files)
                job.PauseFile(file.Id);
        }
    }

    private void SelectFilesByStatus(string key)
    {
        QueueFilesList.SelectedItems.Clear();
        if (filesOwner?.Files == null)
            return;
        foreach (var file in filesOwner.Files)
        {
            if (IsFileStatus(file, key))
                QueueFilesList.SelectedItems.Add(file);
        }
    }

    private bool IsFileStatus(QueueFileItem file, string key) =>
        file != null && file.Status == (string)FindResource(key);

    private static string FilePathOf(QueuedDownload job, QueueFileItem file)
    {
        if (job == null || file == null || string.IsNullOrWhiteSpace(job.Destination) || string.IsNullOrWhiteSpace(file.FileName))
            return "";
        return Path.Combine(job.Destination, file.FileName);
    }

    private static void OpenJobFolder(QueuedDownload job)
    {
        if (string.IsNullOrWhiteSpace(job?.Destination))
            return;
        OpenPath(job.Destination);
    }

    private static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private static void CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        try { Clipboard.SetText(text); } catch { }
    }

    private void UpdateQueueStatus()
    {
        if (QueueStatusText == null)
            return;
        var speed = GlobalConsts.Downloads.Sum(job => ParseSpeed(job.CurrentDownloadSpeed));
        QueueStatusText.Text = string.Format(
            (string)FindResource("QueueStatusLine"),
            GlobalConsts.Downloads.Count,
            QueueGrid.SelectedItems.Count,
            speed.ToString("0.##", CultureInfo.CurrentCulture));
    }

    private static double ParseSpeed(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        var match = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*MiB/s", RegexOptions.IgnoreCase);
        if (!match.Success)
            return 0;
        var number = match.Groups[1].Value.Replace(',', '.');
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static T FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match)
                return match;
            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private void NyBulkLinksTextBox_OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        var text = e.SourceDataObject.GetData(DataFormats.Text, true);
        if (text != null)
        {
            var textAsString = (string)text;
            textAsString += Environment.NewLine;
            BulkLinksTextBox.Text += textAsString;
            BulkLinksTextBox.SelectionStart = BulkLinksTextBox.Text.Length;
            e.CancelCommand();
            e.Handled = true;
        }
    }
}

internal sealed class RelatedPlaylistItem(string title, string url)
{
    public string Title { get; } = title ?? "";
    public string Url { get; } = url ?? "";
    public string Display => string.IsNullOrWhiteSpace(Title) ? Url : $"{Title}  —  {Url}";
}

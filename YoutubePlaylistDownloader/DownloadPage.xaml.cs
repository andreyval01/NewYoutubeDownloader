namespace YoutubePlaylistDownloader;

/// <summary>
/// Interaction logic for DownloadPage.xaml
/// </summary>
public partial class DownloadPage : UserControl, IDisposable, IDownload
{
    private readonly DownloadSettings downloadSettings;
    private FullPlaylist Playlist;
    private string FileType;
    private string VideoSaveFormat;
    private readonly string CaptionsLanguage;
    private int DownloadedCount;
    private readonly int StartIndex;
    private readonly int EndIndex;
    private readonly int Maximum;
    private List<Process> ffmpegList;
    private readonly CancellationTokenSource cts;
    private readonly VideoQuality Quality;
    private string Bitrate;
    private List<Tuple<IVideo, string>> NotDownloaded;
    private IEnumerable<IVideo> Videos;
    private readonly bool AudioOnly;
    private readonly bool PreferHighestFPS;
    private bool DownloadCaptions;
    private readonly bool TagAudioFile;
    private readonly string SavePath;
    const int megaBytes = 1 << 20;
    private readonly bool silent;
    private FixedQueue<double> downloadSpeeds;
    private readonly Dictionary<IVideo, int> indexes = [];
    private readonly List<Task> conversionTasks = [];
    private readonly List<(string Path, string Title)> playbackEntries = [];
    private readonly string jobId;
    private readonly string sourceUrl;
    private DispatcherTimer statusTimer;
    private int nextIndex;
    private bool isPaused;
    private bool slotHeld;
    private string currentVideoId;
    private CancellationTokenSource activeVideoCts;
    private TaskCompletionSource<bool> resumeTcs;
    private TaskCompletionSource<bool> queuePulse = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object orderLock = new();
    private readonly HashSet<string> pausedFiles = new(StringComparer.OrdinalIgnoreCase);
    private List<int> remainingOrder = [];
    private bool queueLoopActive;
    private bool filesChecked;
    private bool fileCheckFailed;
    private readonly HashSet<int> verifiedComplete = [];
    private readonly Dictionary<int, string> plannedOutput = [];

    public bool StillDownloading;
    public string JobId => jobId;
    public bool IsPaused => isPaused;
    public string Destination => SavePath;
    public string Source => sourceUrl;
    public ObservableCollection<QueueFileItem> Files { get; } = [];
    IReadOnlyList<QueueFileItem> IDownload.Files => Files;

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
        get => Maximum;
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


    public DownloadPage(FullPlaylist playlist, DownloadSettings settings, string savePath = "", IEnumerable<IVideo> videos = null,
        bool silent = false, CancellationTokenSource cancellationToken = null, string sourceUrl = "", bool resume = false,
        string jobId = null, bool savePathIsFinal = false, bool startPaused = false, IEnumerable<string> pausedVideoIds = null)
    {
        InitializeComponent();
        downloadSettings = settings;
        downloadSpeeds = new FixedQueue<double>(50);
        this.jobId = string.IsNullOrWhiteSpace(jobId) ? Guid.NewGuid().ToString("N") : jobId;
        this.sourceUrl = InferSourceUrl(playlist, videos, sourceUrl);

        IEnumerable<IVideo> sourceVideos = playlist == null || playlist.BasePlaylist == null ? videos : playlist.Videos;
        sourceVideos ??= [];

        if (settings.FilterVideosByLength)
        {
            sourceVideos = settings.FilterMode ?
                sourceVideos.Where(video => video.Duration is { } duration && duration.TotalMinutes > settings.FilterByLengthValue) :
                sourceVideos.Where(video => video.Duration is { } duration && duration.TotalMinutes < settings.FilterByLengthValue);
        }

        Videos = sourceVideos.ToList();
        if (pausedVideoIds != null)
        {
            foreach (var id in pausedVideoIds)
            {
                if (!string.IsNullOrWhiteSpace(id) && !string.Equals(id, "___________", StringComparison.Ordinal))
                    pausedFiles.Add(id);
            }
        }

        if (startPaused)
        {
            isPaused = true;
            resumeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var count = Videos.Count();
        var startIndex = 0;
        var endIndex = count - 1;
        if (settings.Subset && count > 0)
        {
            var last = count - 1;
            if (settings.SubsetStartIndex > 0)
                startIndex = Math.Min(settings.SubsetStartIndex, last);
            if (settings.SubsetEndIndex > 0)
                endIndex = Math.Min(settings.SubsetEndIndex, last);
            if (endIndex < startIndex)
                endIndex = startIndex;
        }

        this.silent = silent;

        if (!silent)
        {
            GlobalConsts.NyHideSettingsButton();
            GlobalConsts.NyHideAboutButton();
            GlobalConsts.NyHideHomeButton();
            GlobalConsts.NyHideHelpButton();
        }

        cts = cancellationToken ?? new CancellationTokenSource();
        ffmpegList = [];
        StartIndex = startIndex;
        EndIndex = endIndex;
        NotDownloaded = [];
        Maximum = EndIndex - StartIndex + 1;
        DownloadedVideosProgressBar.Maximum = Maximum;
        Playlist = playlist;
        FileType = settings.SaveFormat;
        VideoSaveFormat = settings.VideoSaveFormat;
        DownloadedCount = 0;
        Quality = settings.Quality;
        DownloadCaptions = settings.DownloadCaptions;
        CaptionsLanguage = settings.CaptionsLanguage;
        SavePath = ResolvePlaylistSavePath(savePath, playlist, Videos, settings, savePathIsFinal);
        BuildQueueFiles();

        if (!Directory.Exists(SavePath))
            Directory.CreateDirectory(SavePath);

        AudioOnly = settings.AudioOnly;
        TagAudioFile = settings.TagAudioFile;
        PreferHighestFPS = settings.PreferHighestFPS;

        Bitrate = settings.SetBitrate && !string.IsNullOrWhiteSpace(settings.Bitrate) && settings.Bitrate.All(char.IsDigit)
            ? $"-b:a {settings.Bitrate}k"
            : string.Empty;

        StillDownloading = true;

        var firstVideo = Videos?.FirstOrDefault();
        ImageUrl = firstVideo?.Thumbnails?.TryGetWithHighestResolution()?.Url
            ?? (firstVideo is GenericVideo
                ? ""
                : $"https://img.youtube.com/vi/{firstVideo?.Id}/maxresdefault.jpg");
        Title = playlist?.BasePlaylist?.Title ?? playlist?.Title ?? firstVideo?.Title;
        CurrentTitle = (string)FindResource("Loading");
        TotalDownloaded = $"({DownloadedCount}/{Maximum})";
        CurrentProgressPercent = 0;
        CurrentDownloadSpeed = $"{FindResource("DownloadSpeed")}: 0 MiB/s";
        nextIndex = StartIndex;

        if (resume)
        {
            downloadSettings.SkipExisting = true;
            downloadSettings.OpenDestinationFolderWhenDone = false;
        }

        PersistJob(flushNow: true);
        GlobalConsts.NySaveDownloadSettings();
        GlobalConsts.NySaveConsts();
        foreach (var row in GlobalConsts.Downloads.Where(item => item.JobId == this.jobId && item.Item is SavedJobPlaceholder).ToList())
            GlobalConsts.Downloads.Remove(row);
        StartStatusTimer();

        if (settings.Convert || settings.AudioOnly)
            NyStartDownloadingWithConverting(playlist.BasePlaylist?.Id, cts.Token).ConfigureAwait(false);
        else
            NyStartDownloading(cts.Token).ConfigureAwait(false);

        GlobalConsts.Downloads.Add(new QueuedDownload(this));
    }

    public static async Task NySequenceDownload(
        IEnumerable<string> links,
        DownloadSettings settings,
        bool silent = false,
        string savePath = "",
        bool resume = false,
        string jobId = null,
        bool savePathIsFinal = false,
        bool enqueuePending = true,
        bool startPaused = false,
        IEnumerable<string> pausedVideoIds = null)
    {
        var client = GlobalConsts.YoutubeClient;
        Playlist basePlaylist;
        FullPlaylist fullPlaylist;
        Channel channel;
        IEnumerable<IVideo> videos = new List<IVideo>();
        var notDownloaded = new List<(string, string)>();
        var queue = new List<string>();
        var knownTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            if (MediaSourceHelpers.IsPlaylistCatalog(link))
            {
                try
                {
                    var scan = await PlaylistLinkScanner.ScanAsync(link, CancellationToken.None).ConfigureAwait(false);
                    foreach (var item in scan.Playlists)
                    {
                        queue.Add(item.Url);
                        if (!string.IsNullOrWhiteSpace(item.Title))
                            knownTitles[item.Url] = item.Title;
                    }
                }
                catch (Exception ex)
                {
                    notDownloaded.Add((link, ex.Message));
                    await GlobalConsts.NyLog(ex.ToString(), "SequenceDownload catalog");
                }
            }
            else
            {
                queue.Add(link);
            }
        }

        if (enqueuePending)
        {
            foreach (var link in queue)
            {
                knownTitles.TryGetValue(link, out var pendingTitle);
                DownloadQueueStore.AddPending(link, pendingTitle, settings);
            }
            GlobalConsts.NySaveDownloadSettings();
            GlobalConsts.NySaveConsts();
        }

        foreach (var link in queue)
        {
            async Task NyDownload(FullPlaylist playlistD, IEnumerable<IVideo> videosD)
            {
                DownloadQueueStore.RemovePending(link);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _ = new DownloadPage(
                        playlistD,
                        settings,
                        savePath: savePath,
                        videos: videosD,
                        silent: silent,
                        sourceUrl: link,
                        resume: resume,
                        jobId: jobId,
                        savePathIsFinal: savePathIsFinal,
                        startPaused: startPaused,
                        pausedVideoIds: pausedVideoIds);
                });
            }
            try
            {
                if (YoutubeHelpers.NyTryParsePlaylistId(link, out var playlistId))
                {
                    basePlaylist = await client.Playlists.GetAsync(playlistId.Value).ConfigureAwait(false);
                    fullPlaylist = new FullPlaylist(basePlaylist, await client.Playlists.GetVideosAsync(basePlaylist.Id).CollectAsync().ConfigureAwait(false));
                    await NyDownload(fullPlaylist, new List<IVideo>());
                }
                else if (YoutubeHelpers.NyTryParseChannelId(link, out var channelId))
                {
                    channel = await client.Channels.GetAsync(channelId).ConfigureAwait(false);
                    videos = await client.Channels.GetUploadsAsync(channelId).CollectAsync().ConfigureAwait(false);
                    fullPlaylist = new FullPlaylist(null, null, channel.Title);
                    await NyDownload(fullPlaylist, videos);
                }
                else if (YoutubeHelpers.NyTryParseUsername(link, out var username))
                {
                    channel = await client.Channels.GetByUserAsync(username).ConfigureAwait(false);
                    videos = await client.Channels.GetUploadsAsync(channel.Id).CollectAsync().ConfigureAwait(false);
                    fullPlaylist = new FullPlaylist(null, null, channel.Title);
                    await NyDownload(fullPlaylist, videos);
                }
                else if (YoutubeHelpers.NyTryParseVideoId(link, out var videoId))
                {
                    var video = await client.Videos.GetAsync(videoId);
                    fullPlaylist = new FullPlaylist(null, new[] { video }, video.Title);
                    await NyDownload(fullPlaylist, new[] { video });
                }
                else if (MediaSourceHelpers.TryGetExternalSource(link, out _))
                {
                    if (!YtDlpDownloader.IsAvailable)
                        throw new FileNotFoundException("yt-dlp.exe was not found.", YtDlpDownloader.ExePath);
                    var info = await YtDlpDownloader.GetInfoAsync(link, CancellationToken.None).ConfigureAwait(false);
                    var playlistTitle = info.Title;
                    if (knownTitles.TryGetValue(link, out var knownTitle) && !string.IsNullOrWhiteSpace(knownTitle))
                        playlistTitle = knownTitle;
                    fullPlaylist = new FullPlaylist(null, info.Videos, playlistTitle, info.IsPlaylist);
                    await NyDownload(fullPlaylist, info.Videos);
                }
                else
                {
                    throw new Exception(Application.Current.FindResource("NoVideosToDownload").ToString());
                }
            }
            catch (Exception ex)
            {
                notDownloaded.Add((link, ex.Message));
                await GlobalConsts.NyLog(ex.ToString(), "SequenceDownload at DownloadPage.xaml.cs");
            }
        }

        if (notDownloaded.Any())
        {
            await GlobalConsts.NyShowSelectableDialog($"{Application.Current.FindResource("CouldntDownload")}",
                  string.Concat($"{Application.Current.FindResource("ListOfNotDownloadedVideos")}\n", string.Join("\n", notDownloaded.Select(x => string.Concat(x.Item1, " Reason: ", x.Item2)))),
                  () =>
                  {
                      _ = NySequenceDownload(notDownloaded.Select(x => x.Item1), settings).ConfigureAwait(false);
                      GlobalConsts.MainPage.NyChangeToQueueTab();
                      GlobalConsts.NyLoadPage(GlobalConsts.MainPage.NyLoad());
                  });
        }
    }

    public async Task NyStartDownloadingWithConverting(PlaylistId? playlistId, CancellationToken jobToken)
    {
        var token = jobToken;
        try
        {
            await CheckExistingFilesAsync(jobToken).ConfigureAwait(false);
            if (StartIndex > Videos.Count() - 1)
            {
                await GlobalConsts.NyShowMessage($"{FindResource("NoVideosToDownload")}", $"{FindResource("ThereAreNoVideosToDownload")}");
                StillDownloading = false;
                GlobalConsts.NyLoadPage(GlobalConsts.MainPage.NyLoad());
                return;
            }

            if (!await AcquireDownloadSlotAsync(jobToken).ConfigureAwait(false))
                return;

            ResetRemainingOrder();
            var client = GlobalConsts.YoutubeClient;
            var convertingCount = 0;
            conversionTasks.Clear();
            queueLoopActive = true;
            while (true)
            {
                var i = await NextVideoIndexAsync(jobToken).ConfigureAwait(false);
                if (i < 0)
                    break;

                var video = Videos.ElementAtOrDefault(i);
                if (video == default(IVideo))
                    continue;

                using var videoCts = CancellationTokenSource.CreateLinkedTokenSource(jobToken);
                activeVideoCts = videoCts;
                token = videoCts.Token;
                nextIndex = i;
                indexes[video] = i + 1;
                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        DownloadSpeedTextBlock.Visibility = Visibility.Collapsed;
                        NyUpdate(0, video);
                    });

                    downloadSpeeds.Clear();

                    if (await DownloadGenericIfNeededAsync(video, i, true, token))
                    {
                        NoteDownloaded(i);
                        TotalDownloaded = $"({DownloadedCount}/{Maximum})";
                        continue;
                    }

                    var streamInfoSet = await client.Videos.Streams.GetManifestAsync(video.Id, token);
                    IStreamInfo bestQuality = YoutubeStreamSelector.GetBestAudioOrMuxed(streamInfoSet, downloadSettings.VideoLanguage);
                    var cleanFileNameWithID = GlobalConsts.NyCleanFileName(video.Title + video.Id);
                    var cleanFileName = FileNameFor(video, i);
                    var fileLoc = $"{GlobalConsts.TempFolderPath}{cleanFileNameWithID}";

                    if (AudioOnly)
                        FileType = bestQuality.Container.Name;

                    var outputFileLoc = $"{GlobalConsts.TempFolderPath}{cleanFileNameWithID}.{FileType}";
                    var copyFileLoc = OutputFileFor(video, i, FileType);

                    if (downloadSettings.SkipExisting && File.Exists(copyFileLoc) && (fileCheckFailed || verifiedComplete.Contains(i)))
                    {
                        CurrentStatus = string.Concat(FindResource("Skipping"));
                        CurrentTitle = video.Title;
                        CurrentProgressPercent = 0;
                        CurrentDownloadSpeed = "0 MiB/s";

                        RememberOutput(copyFileLoc, video.Title);
                        NoteDownloaded(i);
                        TotalDownloaded = $"({DownloadedCount}/{Maximum})";

                        continue;
                    }

                    CurrentStatus = string.Concat(FindResource("Downloading"));
                    CurrentTitle = video.Title;
                    CurrentProgressPercent = 0;
                    CurrentDownloadSpeed = "0 MiB/s";

                    if (await TryDownloadWithYouTubeAccountAsync(video.Id, copyFileLoc, true, FileType, token))
                    {
                        RememberOutput(copyFileLoc, video.Title);
                        NoteDownloaded(i);
                        TotalDownloaded = $"({DownloadedCount}/{Maximum})";
                        continue;
                    }

                    using (var stream = new ProgressStream(File.Create(fileLoc)))
                    {
                        Stopwatch sw = new();
                        TimeSpan ts = new(0);
                        var seconds = 1;
                        var downloadSpeedText = (string)FindResource("DownloadSpeed");

                        stream.BytesWritten += async (sender, args) =>
                        {
                            try
                            {
                                var percent = Convert.ToInt32(args.StreamLength * 100 / bestQuality.Size.Bytes);
                                CurrentProgressPercent = percent;
                                double speedInMB = 0;
                                var delta = sw.Elapsed - ts;
                                ts = sw.Elapsed;
                                try
                                {
                                    var speedInBytes = args.BytesMoved / delta.TotalSeconds;
                                    speedInMB = Math.Round(speedInBytes / megaBytes, 2);
                                    downloadSpeeds.NyEnqueue(speedInMB);
                                }
                                catch (DivideByZeroException)
                                {

                                }

                                if (!sw.IsRunning)
                                    sw.Start();

                                await Dispatcher.InvokeAsync(() =>
                                {
                                    try
                                    {
                                        CurrentDownloadProgressBar.Value = percent;
                                        CurrentDownloadProgressBarTextBlock.Text = $"{percent}%";
                                        if (sw.Elapsed.Seconds == seconds && delta.TotalMilliseconds > 0)
                                        {
                                            CurrentDownloadSpeed = string.Concat(downloadSpeedText, Math.Round(downloadSpeeds.Average(), 2), " MiB/s");
                                            DownloadSpeedTextBlock.Text = CurrentDownloadSpeed;
                                            DownloadSpeedTextBlock.Visibility = Visibility.Visible;
                                            seconds += 1;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        GlobalConsts.NyLog(ex.ToString(), "Dispatcher.InvokeAsync at DownloadPage.xaml.cs StartDownloadingWithConverting").Wait();
                                    }
                                }, DispatcherPriority.Normal, cts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                if (token.IsCancellationRequested || isPaused)
                                    throw;
                            }
                            catch (Exception ex)
                            {
                                await GlobalConsts.NyLog(ex.ToString(), "BytesWrittenEventHandler at ProgressStream in DownloadPage");
                            }

                        };
                        await client.Videos.Streams.CopyToAsync(bestQuality, stream, cancellationToken: token);
                        sw.Stop();
                    }
                    if (!AudioOnly)
                    {
                        var ffmpeg = new Process()
                        {
                            EnableRaisingEvents = true,
                            StartInfo = new ProcessStartInfo()
                            {
                                FileName = GlobalConsts.FFmpegFilePath,
                                Arguments = $"-i \"{fileLoc}\" -y {Bitrate} \"{outputFileLoc}\"",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            }
                        };

                        token.ThrowIfCancellationRequested();
                        ffmpeg.Exited += async (x, y) =>
                        {
                            try
                            {
                                ffmpegList?.Remove(ffmpeg);
                                convertingCount--;

                                if (TagAudioFile)
                                {
                                    var videoIndex = indexes[video];
                                    var afterTagName = await GlobalConsts.NyTagFile(video, videoIndex, outputFileLoc, Playlist);
                                    FileType = new string(copyFileLoc.Skip(copyFileLoc.LastIndexOf('.') + 1).ToArray());
                                    if (afterTagName != outputFileLoc)
                                    {
                                        if (playlistId.HasValue)
                                        {
                                            video = new PlaylistVideo(playlistId.Value, video.Id, afterTagName, video.Author, video.Duration, video.Thumbnails);
                                        }

                                        cleanFileName = FileNameFor(video, videoIndex - 1);
                                        copyFileLoc = $"{SavePath}\\{cleanFileName}.{FileType}";
                                    }
                                }
                                var copyFileLocCounter = 1;
                                while (File.Exists(copyFileLoc))
                                {
                                    copyFileLoc = $"{SavePath}\\{cleanFileName}-{copyFileLocCounter}.{FileType}";
                                    copyFileLocCounter++;
                                }
                                File.Copy(outputFileLoc, copyFileLoc, true);
                                File.Delete(outputFileLoc);

                            }
                            catch (Exception ex)
                            {
                                await GlobalConsts.NyLog(ex.ToString(), "DownloadPage with convert");
                            }
                        };
                        if (!GlobalConsts.settings.LimitConversions)
                        {
                            ffmpeg.Start();
                            convertingCount++;
                            ffmpegList.Add(ffmpeg);
                        }
                        else
                        {
                            conversionTasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    convertingCount++;
                                    await GlobalConsts.ConversionsLocker.WaitAsync(cts.Token);
                                    ffmpeg.Start();
                                    ffmpeg.Exited += (x, y) => GlobalConsts.ConversionsLocker.Release();
                                    ffmpegList.Add(ffmpeg);
                                }
                                catch (OperationCanceledException)
                                {
                                    GlobalConsts.ConversionsLocker.Release();
                                }
                                catch (Exception ex)
                                {
                                    await GlobalConsts.NyLog(ex.ToString(), "ConversionsLocker at StartDownloadingWithConverting at DownloadPage.xaml.cs");
                                }
                            }, token));
                        }
                    }
                    else
                    {
                        File.Copy(fileLoc, copyFileLoc, true);

                        File.Delete(fileLoc);
                        try
                        {
                            if (TagAudioFile)
                            {
                                var afterTagName = await GlobalConsts.NyTagFile(video, i + 1, copyFileLoc, Playlist);
                                if (afterTagName != outputFileLoc)
                                {
                                    if (playlistId.HasValue)
                                    {
                                        video = new PlaylistVideo(playlistId.Value, video.Id, afterTagName, video.Author, video.Duration, video.Thumbnails);
                                    }

                                    cleanFileName = FileNameFor(video, i);
                                    copyFileLoc = $"{SavePath}\\{cleanFileName}.{FileType}";
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            await GlobalConsts.NyLog(ex.ToString(), "TagFile at download with convert playlist");
                        }
                    }

                    RememberOutput(copyFileLoc, video.Title);
                    NoteDownloaded(i);
                    TotalDownloaded = $"({DownloadedCount}/{Maximum})";

                }
                catch (OperationCanceledException)
                {
                    token = jobToken;
                    if (jobToken.IsCancellationRequested || !await HandleVideoCanceledAsync(i, video, jobToken).ConfigureAwait(false))
                        goto exit;
                }
                catch (Exception ex)
                {
                    await GlobalConsts.NyLog(ex.ToString(), "DownloadPage DownloadWithConvert");
                    NotDownloaded.Add(new Tuple<IVideo, string>(video, ex.Message));
                    SetFileStatus(video?.Id, "FileError");
                }
                finally
                {
                    token = jobToken;
                    if (ReferenceEquals(activeVideoCts, videoCts))
                        activeVideoCts = null;
                }
            }

        exit:
            queueLoopActive = false;
            token = jobToken;

            if (NotDownloaded.Any())
            {
                await GlobalConsts.NyShowSelectableDialog($"{FindResource("CouldntDownload")}",
                      string.Concat($"{FindResource("ListOfNotDownloadedVideos")}\n", string.Join("\n", NotDownloaded.Select(x => string.Concat(x.Item1.Title, " Reason: ", x.Item2)))),
                      () =>
                      {
                          _ = NySequenceDownload(NotDownloaded.Select(x => x.Item1.Url), downloadSettings).ConfigureAwait(false);
                          GlobalConsts.MainPage.NyChangeToQueueTab();
                          GlobalConsts.NyLoadPage(GlobalConsts.MainPage.NyLoad());
                      });
            }

            while (ffmpegList.Count > 0 || conversionTasks?.Count(x => !x.IsCompleted) > 0)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    var status = string.Concat(FindResource("StillConverting"), " ", convertingCount, " ", FindResource("files"));
                    HeadlineTextBlock.Text = (string)FindResource("AllDone");
                    CurrentDownloadProgressBar.IsIndeterminate = true;
                    TotalDownloadedGrid.Visibility = Visibility.Collapsed;
                    TotalDownloadsProgressBarTextBlock.Text = $"({DownloadedCount} {FindResource("Of")} {Maximum})";
                    DownloadedVideosProgressBar.Value = Maximum;
                    ConvertingTextBlock.Visibility = Visibility.Visible;
                    ConvertingTextBlock.Text = status;
                    CurrentDownloadProgressBarTextBlock.Visibility = Visibility.Collapsed;
                    DownloadSpeedTextBlock.Visibility = Visibility.Collapsed;
                    CurrentStatus = status;
                    CurrentDownloadSpeed = "0 MiB/s";
                    CurrentProgressPercent = 100;
                    Title = string.Concat(FindResource("Converting"));

                });
                await Task.Delay(1000, token);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                var allDone = (string)FindResource("AllDone");
                CurrentDownloadGrid.Visibility = Visibility.Collapsed;
                ConvertingTextBlock.Visibility = Visibility.Collapsed;
                DownloadSpeedTextBlock.Visibility = Visibility.Collapsed;
                CurrentStatus = string.Empty;
                CurrentDownloadSpeed = string.Empty;
                CurrentProgressPercent = 100;
                Title = allDone;
                HeadlineTextBlock.Text = allDone;
                TotalDownloadedGrid.Visibility = Visibility.Collapsed;
                TotalDownloadsProgressBarTextBlock.Text = $"({DownloadedCount} {FindResource("Of")} {Maximum})";
                DownloadedVideosProgressBar.Value = Maximum;
                CurrentDownloadProgressBarTextBlock.Visibility = Visibility.Collapsed;
            });

            if (downloadSettings.OpenDestinationFolderWhenDone)
                NyOpenFolder_Click(null, null);

        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await GlobalConsts.NyLog(ex.ToString(), "DownloadPage With converting");

        }
        finally
        {
            await Task.WhenAll(conversionTasks);
            WritePlaybackPlaylist();
            ReleaseSlot();
            CompletePersistedJob();
            StillDownloading = false;
            if (!disposedValue)
                Dispose();
        }
    }

    public async Task NyStartDownloading(CancellationToken jobToken)
    {
        var token = jobToken;
        try
        {
        await CheckExistingFilesAsync(jobToken).ConfigureAwait(false);
        if (StartIndex > Videos.Count() - 1)
        {
            await GlobalConsts.NyShowMessage($"{FindResource("NoVideosToDownload")}", $"{FindResource("ThereAreNoVideosToDownload")}");
            StillDownloading = false;
            GlobalConsts.NyLoadPage(GlobalConsts.MainPage.NyLoad());
            return;
        }

        if (!await AcquireDownloadSlotAsync(jobToken).ConfigureAwait(false))
            return;

        ResetRemainingOrder();
        var client = GlobalConsts.YoutubeClient;
        var convertingCount = 0;
        conversionTasks.Clear();
        queueLoopActive = true;
        while (true)
        {
            var i = await NextVideoIndexAsync(jobToken).ConfigureAwait(false);
            if (i < 0)
                break;

            var video = Videos.ElementAtOrDefault(i);
            if (video == default(IVideo))
                continue;

            using var videoCts = CancellationTokenSource.CreateLinkedTokenSource(jobToken);
            activeVideoCts = videoCts;
            token = videoCts.Token;
            nextIndex = i;
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    DownloadSpeedTextBlock.Visibility = Visibility.Collapsed;
                    NyUpdate(0, video);
                });

                downloadSpeeds.Clear();

                if (await DownloadGenericIfNeededAsync(video, i, false, token))
                {
                    NoteDownloaded(i);
                    TotalDownloaded = $"({DownloadedCount}/{Maximum})";
                    continue;
                }

                var streamInfoSet = await client.Videos.Streams.GetManifestAsync(video.Id, token);
                IStreamInfo bestQuality = YoutubeStreamSelector.TryGetBestVideoOnly(streamInfoSet, Quality, PreferHighestFPS);
                IStreamInfo bestAudio = YoutubeStreamSelector.TryGetBestAudioOnly(streamInfoSet, downloadSettings.VideoLanguage)
                    ?? YoutubeStreamSelector.TryGetBestMuxed(streamInfoSet);
                var muxedOnly = bestQuality == null;
                if (muxedOnly)
                {
                    bestQuality = YoutubeStreamSelector.TryGetBestMuxed(streamInfoSet)
                        ?? throw new InvalidOperationException("No playable video streams.");
                    bestAudio = null;
                }

                var cleanVideoNameWithId = GlobalConsts.NyCleanFileName(video.Title + video.Id);
                var cleanVideoName = FileNameFor(video, i);
                var fileLoc = $"{GlobalConsts.TempFolderPath}{cleanVideoNameWithId}";
                var outputFileLoc = $"{GlobalConsts.TempFolderPath}{cleanVideoNameWithId}.{VideoSaveFormat}";
                var copyFileLoc = OutputFileFor(video, i, VideoSaveFormat);
                var audioLoc = muxedOnly ? null : $"{GlobalConsts.TempFolderPath}{cleanVideoNameWithId}-audio.{bestAudio.Container.Name}";
                var captionsLoc = $"{GlobalConsts.TempFolderPath}{cleanVideoNameWithId}.srt";

                if (downloadSettings.SkipExisting && File.Exists(copyFileLoc) && (fileCheckFailed || verifiedComplete.Contains(i)))
                {
                    CurrentStatus = string.Concat(FindResource("Skipping"));
                    CurrentTitle = video.Title;
                    CurrentProgressPercent = 0;
                    CurrentDownloadSpeed = "0 MiB/s";

                    RememberOutput(copyFileLoc, video.Title);
                    NoteDownloaded(i);
                    TotalDownloaded = $"({DownloadedCount}/{Maximum})";

                    continue;
                }

                CurrentStatus = string.Concat(FindResource("Downloading"));
                CurrentTitle = video.Title;
                CurrentProgressPercent = 0;
                CurrentDownloadSpeed = "0 MiB/s";

                if (await TryDownloadWithYouTubeAccountAsync(video.Id, copyFileLoc, false, VideoSaveFormat, token))
                {
                    RememberOutput(copyFileLoc, video.Title);
                    NoteDownloaded(i);
                    TotalDownloaded = $"({DownloadedCount}/{Maximum})";
                    continue;
                }

                var ffmpegArguments = "";

                using (var stream = new ProgressStream(File.Create(fileLoc)))
                {
                    Stopwatch sw = new();
                    TimeSpan ts = new(0);
                    var seconds = 1;
                    var downloadSpeedText = (string)FindResource("DownloadSpeed");

                    stream.BytesWritten += async (sender, args) =>
                    {
                        try
                        {
                            var percent = Convert.ToInt32(args.StreamLength * 100 / bestQuality.Size.Bytes);
                            CurrentProgressPercent = percent;
                            double speedInMB = 0;
                            var delta = sw.Elapsed - ts;
                            ts = sw.Elapsed;
                            try
                            {
                                var speedInBytes = args.BytesMoved / delta.TotalSeconds;
                                speedInMB = Math.Round(speedInBytes / megaBytes, 2);
                                downloadSpeeds.NyEnqueue(speedInMB);
                            }
                            catch (DivideByZeroException)
                            {

                            }
                            if (!sw.IsRunning)
                                sw.Start();

                            await Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    CurrentDownloadProgressBar.Value = percent;
                                    CurrentDownloadProgressBarTextBlock.Text = $"{percent}%";
                                    if (sw.Elapsed.Seconds == seconds && delta.TotalMilliseconds > 0)
                                    {
                                        var speed = string.Concat(downloadSpeedText, Math.Round(downloadSpeeds.Average(), 2), " MiB/s");
                                        DownloadSpeedTextBlock.Text = speed;
                                        DownloadSpeedTextBlock.Visibility = Visibility.Visible;
                                        CurrentDownloadSpeed = speed;
                                        seconds += 1;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    GlobalConsts.NyLog(ex.ToString(), "Dispatcher.InvokeAsync at DownloadPage.xaml.cs StartDownloading").Wait();
                                }
                            }, DispatcherPriority.Normal, cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            if (token.IsCancellationRequested || isPaused)
                                throw;
                        }
                        catch (Exception ex)
                        {
                            await GlobalConsts.NyLog(ex.ToString(), "BytesWrittenEventHandler at ProgressStream in DownloadPage");
                        }

                    };
                    try
                    {
                        await client.Videos.Streams.CopyToAsync(bestQuality, stream, cancellationToken: token).AsTask().ConfigureAwait(false);

                        if (!muxedOnly)
                        {
                            await using var audioStream = File.Create(audioLoc);
                            await client.Videos.Streams.CopyToAsync(bestAudio, audioStream, cancellationToken: token).AsTask().ConfigureAwait(false);
                        }
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        var muxed = YoutubeStreamSelector.TryGetBestMuxed(streamInfoSet)
                            ?? throw new InvalidOperationException("No playable video streams.", ex);
                        stream.SetLength(0);
                        stream.Position = 0;
                        bestQuality = muxed;
                        muxedOnly = true;
                        audioLoc = null;
                        await client.Videos.Streams.CopyToAsync(muxed, stream, cancellationToken: token).AsTask().ConfigureAwait(false);
                    }

                    var includeCaptions = false;
                    if (DownloadCaptions)
                    {
                        var captionsInfo = await client.Videos.ClosedCaptions.GetManifestAsync(video.Id, token).ConfigureAwait(false);
                        var captions = captionsInfo.TryGetByLanguage(CaptionsLanguage);
                        if (captions == null)
                        {
                            DownloadCaptions = false;
                        }
                        else
                        {
                            includeCaptions = true;
                            await client.Videos.ClosedCaptions.DownloadAsync(captions, captionsLoc, cancellationToken: token).AsTask().ConfigureAwait(false);
                        }
                    }
                    sw.Stop();

                    if (muxedOnly)
                    {
                        ffmpegArguments = includeCaptions && VideoSaveFormat == "mkv"
                            ? $"-i \"{fileLoc}\" -i \"{captionsLoc}\" -y -c copy \"{outputFileLoc}\""
                            : $"-i \"{fileLoc}\" -y -c copy \"{outputFileLoc}\"";
                    }
                    else if (includeCaptions && VideoSaveFormat == "mkv")
                    {
                        ffmpegArguments = $"-i \"{fileLoc}\" -i \"{audioLoc}\" -i \"{captionsLoc}\" -y -map 0:v:0 -map 1:a:0 -map 2 -c copy \"{outputFileLoc}\"";
                    }
                    else
                    {
                        ffmpegArguments = $"-i \"{fileLoc}\" -i \"{audioLoc}\" -y -map 0:v:0 -map 1:a:0 -c copy \"{outputFileLoc}\"";
                    }

                    if (includeCaptions && VideoSaveFormat != "mkv")
                        File.Copy(captionsLoc, $"{SavePath}\\{cleanVideoName}.srt");
                }

                var ffmpeg = new Process()
                {
                    EnableRaisingEvents = true,
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = GlobalConsts.FFmpegFilePath,
                        Arguments = ffmpegArguments,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                    }
                };

                token.ThrowIfCancellationRequested();
                ffmpeg.Exited += async (x, y) =>
                {
                    try
                    {
                        ffmpegList?.Remove(ffmpeg);
                        convertingCount--;
                        var copyFileLocCounter = 1;
                        while (File.Exists(copyFileLoc))
                        {
                            copyFileLoc = $"{SavePath}\\{cleanVideoName}-{copyFileLocCounter}.{VideoSaveFormat}";
                            copyFileLocCounter++;
                        }
                        File.Copy(outputFileLoc, copyFileLoc, true);

                        File.Delete(outputFileLoc);
                        if (!string.IsNullOrEmpty(audioLoc) && File.Exists(audioLoc))
                            File.Delete(audioLoc);
                        File.Delete(fileLoc);
                    }
                    catch (Exception ex)
                    {
                        await GlobalConsts.NyLog(ex.ToString(), "DownloadPage without convert");
                    }
                };

                if (!GlobalConsts.settings.LimitConversions)
                {
                    ffmpeg.Start();
                    convertingCount++;
                    ffmpegList.Add(ffmpeg);
                }
                else
                {
                    conversionTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            convertingCount++;
                            await GlobalConsts.ConversionsLocker.WaitAsync(cts.Token);
                            ffmpeg.Start();
                            ffmpeg.Exited += (x, y) => GlobalConsts.ConversionsLocker.Release();
                            ffmpegList.Add(ffmpeg);
                        }
                        catch (OperationCanceledException)
                        {
                            GlobalConsts.ConversionsLocker.Release();
                        }
                        catch (Exception ex)
                        {
                            await GlobalConsts.NyLog(ex.ToString(), "ConversionsLocker at StartDownloading at DownloadPage.xaml.cs");
                        }
                    }, token));
                }

                RememberOutput(copyFileLoc, video.Title);
                NoteDownloaded(i);
                TotalDownloaded = $"({DownloadedCount}/{Maximum})";

            }
            catch (OperationCanceledException)
            {
                token = jobToken;
                if (jobToken.IsCancellationRequested || !await HandleVideoCanceledAsync(i, video, jobToken).ConfigureAwait(false))
                    goto exit;
            }
            catch (Exception ex)
            {
                await GlobalConsts.NyLog(ex.ToString(), "DownloadPage DownloadWithConvert");
                NotDownloaded.Add(new Tuple<IVideo, string>(video, ex.Message));
                SetFileStatus(video?.Id, "FileError");
            }
            finally
            {
                token = jobToken;
                if (ReferenceEquals(activeVideoCts, videoCts))
                    activeVideoCts = null;
            }
        }

    exit:
        queueLoopActive = false;
        token = jobToken;

        if (NotDownloaded.Any())
        {
            await GlobalConsts.NyShowSelectableDialog($"{FindResource("CouldntDownload")}",
                  string.Concat($"{FindResource("ListOfNotDownloadedVideos")}\n", string.Join("\n", NotDownloaded.Select(x => string.Concat(x.Item1.Title, " Reason: ", x.Item2)))),
                  () =>
                  {
                      _ = NySequenceDownload(NotDownloaded.Select(x => x.Item1.Url), downloadSettings).ConfigureAwait(false);
                      GlobalConsts.MainPage.NyChangeToQueueTab();
                      GlobalConsts.NyLoadPage(GlobalConsts.MainPage.NyLoad());
                  });
        }

        while (ffmpegList.Count > 0 || conversionTasks?.Count(x => !x.IsCompleted) > 0)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var status = string.Concat(FindResource("StillConverting"), " ", convertingCount, " ", FindResource("files"));
                HeadlineTextBlock.Text = (string)FindResource("AllDone");
                CurrentDownloadProgressBar.IsIndeterminate = true;
                TotalDownloadedGrid.Visibility = Visibility.Collapsed;
                TotalDownloadsProgressBarTextBlock.Text = $"({DownloadedCount} {FindResource("Of")} {Maximum})";
                DownloadedVideosProgressBar.Value = Maximum;
                ConvertingTextBlock.Visibility = Visibility.Visible;
                ConvertingTextBlock.Text = status;
                CurrentDownloadProgressBarTextBlock.Visibility = Visibility.Collapsed;
                DownloadSpeedTextBlock.Visibility = Visibility.Collapsed;
                CurrentStatus = status;
                CurrentDownloadSpeed = "0 MiB/s";
                CurrentProgressPercent = 100;
                Title = string.Concat(FindResource("Converting"));
            });
            await Task.Delay(1000, token);
        }

        StillDownloading = false;

        await Dispatcher.InvokeAsync(() =>
        {
            var allDone = (string)FindResource("AllDone");
            CurrentDownloadGrid.Visibility = Visibility.Collapsed;
            ConvertingTextBlock.Visibility = Visibility.Collapsed;
            DownloadSpeedTextBlock.Visibility = Visibility.Collapsed;
            CurrentStatus = string.Empty;
            CurrentDownloadSpeed = string.Empty;
            CurrentProgressPercent = 100;
            Title = allDone;
            HeadlineTextBlock.Text = allDone;
            TotalDownloadedGrid.Visibility = Visibility.Collapsed;
            TotalDownloadsProgressBarTextBlock.Text = $"({DownloadedCount} {FindResource("Of")} {Maximum})";
            DownloadedVideosProgressBar.Value = Maximum;
            CurrentDownloadProgressBarTextBlock.Visibility = Visibility.Collapsed;
        });

        await Task.WhenAll(conversionTasks);
        WritePlaybackPlaylist();

        if (downloadSettings.OpenDestinationFolderWhenDone)
            NyOpenFolder_Click(null, null);

        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await GlobalConsts.NyLog(ex.ToString(), "DownloadPage without convert");
        }
        finally
        {
            ReleaseSlot();
            CompletePersistedJob();
            StillDownloading = false;
            if (!disposedValue)
                Dispose();
        }
    }

    private void NyBackground_Exit(object sender, RoutedEventArgs e)
    {
        SendToBackground();
    }

    private static void SendToBackground()
    {
        GlobalConsts.MainPage.NyChangeToQueueTab();
        GlobalConsts.NyLoadPage(GlobalConsts.MainPage.NyLoad());
    }

    public static async Task ResumePersistedJobsAsync()
    {
        DownloadQueueSnapshot snapshot;
        try
        {
            snapshot = DownloadQueueStore.GetSnapshot();
        }
        catch
        {
            return;
        }

        if ((snapshot.Jobs?.Count ?? 0) == 0 && (snapshot.Pending?.Count ?? 0) == 0)
            return;

        var savedJobs = (snapshot.Jobs ?? []).OrderBy(x => x.SortOrder).ToList();
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            GlobalConsts.MainPage?.NyChangeToQueueTab();
            foreach (var job in savedJobs)
            {
                if (string.IsNullOrWhiteSpace(job.Url) || GlobalConsts.Downloads.Any(item => item.JobId == job.Id))
                    continue;
                GlobalConsts.Downloads.Add(new QueuedDownload(new SavedJobPlaceholder(
                    job.Id,
                    job.Title,
                    job.CurrentStatus,
                    job.SavePath,
                    job.Url,
                    $"({job.DownloadedCount}/{job.TotalCount})",
                    job.CurrentProgressPercent,
                    job.Paused)));
            }
        });

        foreach (var job in savedJobs)
        {
            if (string.IsNullOrWhiteSpace(job.Url))
                continue;
            try
            {
                var settings = job.Settings?.NyClone() ?? GlobalConsts.DownloadSettings.NyClone();
                settings.SkipExisting = true;
                settings.OpenDestinationFolderWhenDone = false;
                settings.Subset = false;
                settings.SubsetStartIndex = 0;
                settings.SubsetEndIndex = 0;
                await NySequenceDownload(
                    [job.Url],
                    settings,
                    silent: true,
                    savePath: job.SavePath,
                    resume: true,
                    jobId: job.Id,
                    savePathIsFinal: true,
                    enqueuePending: false,
                    startPaused: job.Paused,
                    pausedVideoIds: job.PausedVideoIds).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await GlobalConsts.NyLog(ex.ToString(), "ResumePersistedJobs job").ConfigureAwait(false);
            }
        }

        foreach (var item in snapshot.Pending ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Url))
                continue;
            if (snapshot.Jobs?.Any(job => string.Equals(job.Url, item.Url, StringComparison.OrdinalIgnoreCase)) == true)
                continue;
            try
            {
                var settings = item.Settings?.NyClone() ?? GlobalConsts.DownloadSettings.NyClone();
                settings.SkipExisting = true;
                settings.OpenDestinationFolderWhenDone = false;
                await NySequenceDownload([item.Url], settings, silent: true, enqueuePending: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await GlobalConsts.NyLog(ex.ToString(), "ResumePersistedJobs pending").ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> AcquireDownloadSlotAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await WaitIfPausedAsync(token).ConfigureAwait(false);
                if (slotHeld)
                    return true;

                CurrentStatus = (string)FindResource("WaitingInQueue");
                PersistJob(flushNow: true);
                await DownloadGate.WaitAsync(jobId, token).ConfigureAwait(false);
                if (isPaused)
                {
                    DownloadGate.Release();
                    continue;
                }

                slotHeld = true;
                CurrentStatus = (string)FindResource("Downloading");
                PersistJob(flushNow: true);
                return true;
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void ReleaseSlot()
    {
        if (!slotHeld)
            return;
        slotHeld = false;
        DownloadGate.Release();
    }

    private void ResetRemainingOrder()
    {
        lock (orderLock)
        {
            remainingOrder = [];
            for (var i = StartIndex; i <= EndIndex; i++)
            {
                if (!verifiedComplete.Contains(i))
                    remainingOrder.Add(i);
            }

            nextIndex = remainingOrder.Count == 0 ? EndIndex + 1 : remainingOrder[0];
        }
    }

    private async Task<int> NextVideoIndexAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !disposedValue)
        {
            await WaitIfPausedAsync(token).ConfigureAwait(false);
            if (!slotHeld && !await AcquireDownloadSlotAsync(token).ConfigureAwait(false))
                return -1;

            int index = -1;
            string videoId = null;
            var allPaused = false;
            lock (orderLock)
            {
                if (remainingOrder.Count == 0)
                    return -1;

                for (var n = 0; n < remainingOrder.Count; n++)
                {
                    var candidate = Videos.ElementAtOrDefault(remainingOrder[n]);
                    if (candidate != null && !pausedFiles.Contains(TrackId(candidate)))
                    {
                        index = remainingOrder[n];
                        videoId = TrackId(candidate);
                        remainingOrder.RemoveAt(n);
                        break;
                    }
                }

                allPaused = index < 0;
            }

            if (allPaused)
            {
                ReleaseSlot();
                CurrentStatus = (string)FindResource("Paused");
                PersistJob(false);
                await WaitForQueueChangeAsync(token).ConfigureAwait(false);
                continue;
            }

            currentVideoId = videoId;
            SetFileStatus(videoId, "FileDownloading");
            var video = Videos.ElementAt(index);
            CurrentTitle = video.Title;
            CurrentStatus = (string)FindResource("Downloading");
            PersistJob(false);
            return index;
        }

        return -1;
    }

    private async Task WaitIfPausedAsync(CancellationToken token)
    {
        while (isPaused && !token.IsCancellationRequested && !disposedValue)
        {
            ReleaseSlot();
            CurrentStatus = (string)FindResource("Paused");
            CurrentDownloadSpeed = "";
            PersistJob(true);
            var tcs = resumeTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var reg = token.Register(() => tcs.TrySetCanceled(token));
            await tcs.Task.ConfigureAwait(false);
        }
    }

    private async Task WaitForQueueChangeAsync(CancellationToken token)
    {
        var tcs = queuePulse;
        using var reg = token.Register(() => tcs.TrySetCanceled(token));
        await tcs.Task.ConfigureAwait(false);
    }

    private void PulseQueue()
    {
        var tcs = queuePulse;
        queuePulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.TrySetResult(true);
    }

    private async Task<bool> HandleVideoCanceledAsync(int index, IVideo video, CancellationToken token)
    {
        if (token.IsCancellationRequested || disposedValue)
            return false;

        if (video != null)
        {
            lock (orderLock)
            {
                if (!remainingOrder.Contains(index))
                    remainingOrder.Insert(0, index);
            }

            SetFileStatus(TrackId(video), isPaused || pausedFiles.Contains(TrackId(video)) ? "FilePaused" : "FileQueued");
        }

        if (isPaused)
            await WaitIfPausedAsync(token).ConfigureAwait(false);
        if (!slotHeld && !await AcquireDownloadSlotAsync(token).ConfigureAwait(false))
            return false;
        return !token.IsCancellationRequested;
    }

    public void Pause()
    {
        if (isPaused || disposedValue)
            return;
        isPaused = true;
        resumeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CurrentStatus = (string)FindResource("Paused");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPaused)));
        try { activeVideoCts?.Cancel(); } catch { }
        PersistJob(true);
    }

    public void Resume()
    {
        if (!isPaused)
            return;
        isPaused = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPaused)));
        var tcs = resumeTcs;
        resumeTcs = null;
        tcs?.TrySetResult(true);
        PulseQueue();
        CurrentStatus = (string)FindResource("WaitingInQueue");
        PersistJob(true);
    }

    public void PauseFile(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return;
        lock (orderLock)
            pausedFiles.Add(videoId);
        SetFileStatus(videoId, "FilePaused");
        if (string.Equals(currentVideoId, videoId, StringComparison.OrdinalIgnoreCase))
        {
            try { activeVideoCts?.Cancel(); } catch { }
        }

        PulseQueue();
        PersistJob(true);
    }

    public void ResumeFile(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return;
        lock (orderLock)
            pausedFiles.Remove(videoId);
        SetFileStatus(videoId, "FileQueued");
        PulseQueue();
        PersistJob(true);
    }

    public void PrioritizeFile(string videoId)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return;
        lock (orderLock)
        {
            pausedFiles.Remove(videoId);
            var idx = -1;
            for (var n = 0; n < remainingOrder.Count; n++)
            {
                var candidate = Videos.ElementAtOrDefault(remainingOrder[n]);
                if (candidate != null && string.Equals(TrackId(candidate), videoId, StringComparison.OrdinalIgnoreCase))
                {
                    idx = n;
                    break;
                }
            }

            if (idx > 0)
            {
                var value = remainingOrder[idx];
                remainingOrder.RemoveAt(idx);
                remainingOrder.Insert(0, value);
            }
        }

        SetFileStatus(videoId, "FileQueued");
        PulseQueue();
        PersistJob(true);
    }

    public bool RetryFile(string videoId)
    {
        if (!queueLoopActive || disposedValue || Videos == null || string.IsNullOrWhiteSpace(videoId))
            return false;

        var list = Videos.ToList();
        var index = -1;
        for (var i = StartIndex; i <= EndIndex && i < list.Count; i++)
        {
            if (string.Equals(TrackId(list[i]), videoId, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return false;

        var errorText = (string)FindResource("FileError");
        var matches = false;
        foreach (var file in Files)
        {
            if (string.Equals(file.Id, videoId, StringComparison.OrdinalIgnoreCase) && file.Status == errorText)
                matches = true;
        }

        if (!matches)
            return false;

        lock (orderLock)
        {
            pausedFiles.Remove(videoId);
            if (!remainingOrder.Contains(index))
                remainingOrder.Insert(0, index);
        }

        SetFileStatus(videoId, "FileQueued");
        PulseQueue();
        PersistJob(true);
        return true;
    }

    private void BuildQueueFiles()
    {
        Files.Clear();
        var list = Videos?.ToList() ?? [];
        var ext = downloadSettings.AudioOnly ? downloadSettings.SaveFormat : downloadSettings.VideoSaveFormat;
        for (var i = StartIndex; i <= EndIndex && i < list.Count; i++)
        {
            var video = list[i];
            var fileName = video.Title;
            try
            {
                fileName = FileNameFor(video, i) + "." + ext;
            }
            catch
            {
            }

            Files.Add(new QueueFileItem
            {
                Id = TrackId(video),
                Number = i + 1,
                Title = video.Title,
                FileName = fileName,
                Status = pausedFiles.Contains(TrackId(video))
                    ? (string)FindResource("FilePaused")
                    : (string)FindResource("FileQueued")
            });
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Files)));
    }

    private void SetFileStatus(string videoId, string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(videoId) || string.IsNullOrWhiteSpace(resourceKey))
            return;
        var text = (string)FindResource(resourceKey);
        void Apply()
        {
            foreach (var file in Files)
            {
                if (string.Equals(file.Id, videoId, StringComparison.OrdinalIgnoreCase))
                    file.Status = text;
            }
        }

        if (Dispatcher.CheckAccess())
            Apply();
        else
            _ = Dispatcher.InvokeAsync(Apply);
    }

    private void NoteDownloaded(int index)
    {
        DownloadedCount++;
        lock (orderLock)
            nextIndex = remainingOrder.Count == 0 ? EndIndex + 1 : remainingOrder.Min();
        TotalDownloaded = $"({DownloadedCount}/{Maximum})";
        var done = Videos?.ElementAtOrDefault(index);
        if (done != null)
            SetFileStatus(TrackId(done), "FileDone");
        PersistJob(flushNow: true);
    }

    private void PersistJob(bool flushNow)
    {
        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(sourceUrl))
            return;

        DownloadQueueStore.UpsertJob(new PersistedDownloadJob
        {
            Id = jobId,
            Url = sourceUrl,
            Title = Title,
            SavePath = SavePath,
            ImageUrl = ImageUrl,
            Settings = downloadSettings,
            DownloadedCount = DownloadedCount,
            TotalCount = Maximum,
            NextIndex = nextIndex,
            CurrentTitle = CurrentTitle,
            CurrentStatus = CurrentStatus,
            CurrentProgressPercent = CurrentProgressPercent,
            StillDownloading = StillDownloading,
            Paused = isPaused,
            SortOrder = QueueIndex(),
            PausedVideoIds = CopyPausedFiles()
        }, flushNow);
    }

    private List<string> CopyPausedFiles()
    {
        lock (orderLock)
            return pausedFiles.ToList();
    }

    private int QueueIndex()
    {
        var list = GlobalConsts.Downloads;
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i].Item, this))
                return i;
        }

        return 1_000_000;
    }

    private void CompletePersistedJob()
    {
        StopStatusTimer();
        if (!string.IsNullOrWhiteSpace(jobId))
            DownloadQueueStore.RemoveJob(jobId);
        if (!string.IsNullOrWhiteSpace(sourceUrl))
            DownloadQueueStore.RemovePending(sourceUrl);
    }

    private void StartStatusTimer()
    {
        statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        statusTimer.Tick += StatusTimer_Tick;
        statusTimer.Start();
    }

    private void StatusTimer_Tick(object sender, EventArgs e) => PersistJob(flushNow: false);

    private void StopStatusTimer()
    {
        if (statusTimer == null)
            return;
        statusTimer.Stop();
        statusTimer.Tick -= StatusTimer_Tick;
        statusTimer = null;
    }

    private static string InferSourceUrl(FullPlaylist playlist, IEnumerable<IVideo> videos, string sourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(sourceUrl))
            return sourceUrl.Trim();
        if (playlist?.BasePlaylist != null)
            return $"https://www.youtube.com/playlist?list={playlist.BasePlaylist.Id}";
        var first = videos?.FirstOrDefault() ?? playlist?.Videos?.FirstOrDefault();
        if (first is GenericVideo generic && !string.IsNullOrWhiteSpace(generic.Url))
            return generic.Url;
        if (first != null && first is not GenericVideo)
            return $"https://www.youtube.com/watch?v={first.Id}";
        return "";
    }

    private static string ResolvePlaylistSavePath(
        string savePath,
        FullPlaylist playlist,
        IEnumerable<IVideo> videos,
        DownloadSettings settings,
        bool pathIsFinal = false)
    {
        var root = string.IsNullOrWhiteSpace(savePath) ? GlobalConsts.settings.SaveDirectory : savePath;
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        var videoCount = videos?.Count() ?? 0;
        var isPlaylist = playlist?.IsPlaylist == true || playlist?.BasePlaylist != null || videoCount > 1;
        if (pathIsFinal && !string.IsNullOrWhiteSpace(savePath))
            return savePath;

        if (!isPlaylist || !settings.SavePlaylistsInDifferentDirectories)
            return root;

        var name = playlist?.Title;
        if (string.IsNullOrWhiteSpace(name))
            name = playlist?.BasePlaylist?.Title;
        if (string.IsNullOrWhiteSpace(name))
            name = videos?.FirstOrDefault()?.Author?.ChannelTitle;
        if (string.IsNullOrWhiteSpace(name))
            name = "Playlist";

        name = GlobalConsts.NyCleanFileName(name).Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(name))
            name = "Playlist";

        return Path.Combine(root, name);
    }

    private static string TrackId(IVideo video)
    {
        if (video is GenericVideo generic && !string.IsNullOrWhiteSpace(generic.SourceId))
            return generic.SourceId;
        return video?.Id.ToString() ?? "";
    }

    private string OutputFileFor(IVideo video, int index, string extension)
    {
        if (plannedOutput.TryGetValue(index, out var planned) && !string.IsNullOrWhiteSpace(planned))
            return planned;
        return Path.Combine(SavePath, FileNameFor(video, index) + "." + extension);
    }

    private static void ParkIfBlocking(string outputFile)
    {
        if (string.IsNullOrWhiteSpace(outputFile) || !File.Exists(outputFile))
            return;
        if (File.Exists(outputFile + ".part"))
            return;

        try
        {
            var dest = outputFile + ".incomplete";
            if (File.Exists(dest))
                File.Delete(dest);
            File.Move(outputFile, dest);
        }
        catch
        {
        }
    }

    private async Task CheckExistingFilesAsync(CancellationToken token)
    {
        if (filesChecked)
            return;
        filesChecked = true;
        if (!downloadSettings.SkipExisting || string.IsNullOrWhiteSpace(SavePath))
        {
            fileCheckFailed = true;
            return;
        }

        try
        {
            CurrentStatus = (string)FindResource("CheckingFiles");
            var list = Videos?.ToList() ?? [];
            var extension = downloadSettings.AudioOnly ? FileType : VideoSaveFormat;
            var requests = new List<DownloadedFileCheck.Request>();
            for (var i = StartIndex; i <= EndIndex && i < list.Count; i++)
            {
                var canonical = "";
                try
                {
                    canonical = Path.Combine(SavePath, FileNameFor(list[i], i) + "." + extension);
                }
                catch
                {
                }

                requests.Add(new DownloadedFileCheck.Request(i, i + 1, canonical, list[i].Duration));
            }

            var verdicts = await DownloadedFileCheck.InspectAsync(SavePath, requests, token).ConfigureAwait(false);
            var updates = new List<(string Id, string Key, string Name)>();
            verifiedComplete.Clear();
            plannedOutput.Clear();
            foreach (var verdict in verdicts)
            {
                if (verdict.Index < 0 || verdict.Index >= list.Count)
                    continue;

                var video = list[verdict.Index];
                var id = TrackId(video);
                if (verdict.Complete)
                {
                    verifiedComplete.Add(verdict.Index);
                    updates.Add((id, "FileDone", verdict.DisplayName));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(verdict.OutputPath))
                    plannedOutput[verdict.Index] = verdict.OutputPath;

                var paused = pausedFiles.Contains(id);
                updates.Add((id, paused ? "FilePaused" : verdict.Partial ? "FilePartial" : "FileQueued", verdict.DisplayName));
            }

            DownloadedCount = verifiedComplete.Count;
            TotalDownloaded = $"({DownloadedCount}/{Maximum})";
            CurrentProgressPercent = Maximum <= 0 ? 0 : (int)Math.Round(DownloadedCount * 100.0 / Maximum);
            ApplyFilePresentation(updates);
            PersistJob(true);
        }
        catch (OperationCanceledException)
        {
            fileCheckFailed = true;
        }
        catch (Exception ex)
        {
            fileCheckFailed = true;
            await GlobalConsts.NyLog(ex.ToString(), "CheckExistingFiles").ConfigureAwait(false);
        }
    }

    private void ApplyFilePresentation(IReadOnlyList<(string Id, string Key, string Name)> updates)
    {
        if (updates == null || updates.Count == 0)
            return;

        void Apply()
        {
            foreach (var update in updates)
            {
                var text = (string)FindResource(update.Key);
                foreach (var file in Files)
                {
                    if (!string.Equals(file.Id, update.Id, StringComparison.OrdinalIgnoreCase))
                        continue;
                    file.Status = text;
                    if (!string.IsNullOrWhiteSpace(update.Name))
                        file.FileName = update.Name;
                }
            }
        }

        if (Dispatcher.CheckAccess())
            Apply();
        else
            Dispatcher.Invoke(Apply);
    }

    private string FileNameFor(IVideo video, int index)
        => GlobalConsts.NyCleanFileName(downloadSettings.NyGetFilenameByPattern(video, index, title, Playlist, Maximum));

    private void RememberOutput(string path, string entryTitle)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        playbackEntries.RemoveAll(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        playbackEntries.Add((path, entryTitle ?? Path.GetFileNameWithoutExtension(path)));
    }

    private void WritePlaybackPlaylist()
    {
        if (Maximum <= 1 && Playlist?.BasePlaylist == null)
            return;

        var entries = playbackEntries
            .Where(x => File.Exists(x.Path))
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();
        if (entries.Count == 0)
            return;

        var playlistName = Playlist?.Title ?? Playlist?.BasePlaylist?.Title ?? Title ?? "Playlist";
        playlistName = GlobalConsts.NyCleanFileName(playlistName).Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(playlistName))
            playlistName = "Playlist";

        var m3uPath = Path.Combine(SavePath, playlistName + ".m3u");
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        foreach (var entry in entries)
        {
            var display = (entry.Title ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (string.IsNullOrWhiteSpace(display))
                display = Path.GetFileNameWithoutExtension(entry.Path);
            sb.AppendLine("#EXTINF:-1," + display);
            sb.AppendLine(Path.GetFileName(entry.Path));
        }

        File.WriteAllText(m3uPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private async Task<bool> DownloadGenericIfNeededAsync(IVideo video, int index, bool audioOutput, CancellationToken token)
    {
        if (video is not GenericVideo)
            return false;
        if (!YtDlpDownloader.IsAvailable)
            throw new FileNotFoundException("yt-dlp.exe was not found.", YtDlpDownloader.ExePath);

        var format = audioOutput ? FileType : VideoSaveFormat;
        var copyFileLoc = OutputFileFor(video, index, format);

        if (downloadSettings.SkipExisting && File.Exists(copyFileLoc) && (fileCheckFailed || verifiedComplete.Contains(index)))
        {
            CurrentStatus = string.Concat(FindResource("Skipping"));
            CurrentTitle = video.Title;
            CurrentProgressPercent = 0;
            CurrentDownloadSpeed = "0 MiB/s";
            RememberOutput(copyFileLoc, video.Title);
            return true;
        }

        if (!fileCheckFailed)
            ParkIfBlocking(copyFileLoc);
        CurrentStatus = string.Concat(FindResource("Downloading"));
        CurrentTitle = video.Title;
        CurrentProgressPercent = 0;
        CurrentDownloadSpeed = "0 MiB/s";

        await DownloadWithYtDlpAsync(
            video.Url,
            copyFileLoc,
            audioOutput,
            format,
            youtubeAccount: false,
            token
        );

        if (audioOutput && TagAudioFile && File.Exists(copyFileLoc))
        {
            try
            {
                await GlobalConsts.NyTagFile(video, index + 1, copyFileLoc, Playlist);
            }
            catch (Exception ex)
            {
                await GlobalConsts.NyLog(ex.ToString(), "TagFile at generic download");
            }
        }

        RememberOutput(copyFileLoc, video.Title);
        return true;
    }

    private async Task<bool> TryDownloadWithYouTubeAccountAsync(
        string videoId,
        string outputFile,
        bool audioOnly,
        string format,
        CancellationToken token
    )
    {
        if (!YoutubeSession.Current.IsSignedIn || !YtDlpDownloader.IsAvailable)
            return false;

        await DownloadWithYtDlpAsync(videoId, outputFile, audioOnly, format, youtubeAccount: true, token);
        return true;
    }

    private async Task DownloadWithYtDlpAsync(
        string urlOrId,
        string outputFile,
        bool audioOnly,
        string format,
        bool youtubeAccount,
        CancellationToken token
    )
    {
        var speedLabel = (string)FindResource("DownloadSpeed");
        var progress = new Progress<(int Percent, string Speed)>(p =>
        {
            CurrentProgressPercent = p.Percent;
            _ = Dispatcher.InvokeAsync(() =>
            {
                CurrentDownloadProgressBar.Value = p.Percent;
                CurrentDownloadProgressBarTextBlock.Text = $"{p.Percent}%";
                if (string.IsNullOrWhiteSpace(p.Speed))
                    return;
                CurrentDownloadSpeed = string.Concat(speedLabel, p.Speed);
                DownloadSpeedTextBlock.Text = CurrentDownloadSpeed;
                DownloadSpeedTextBlock.Visibility = Visibility.Visible;
            });
        });

        await YtDlpDownloader.DownloadAsync(
            urlOrId,
            outputFile,
            audioOnly,
            format,
            format,
            progress,
            token,
            youtubeAccount,
            downloadSettings.PreferQuality ? Quality.MaxHeight : null
        );
    }

    private void NyUpdate(int percent, IVideo video)
    {
        CurrentDownloadProgressBar.Value = percent;
        HeadlineTextBlock.Text = (string)FindResource("CurrentlyDownloading") + video.Title;
        CurrentDownloadProgressBarTextBlock.Text = $"{percent}%";
        TotalDownloadsProgressBarTextBlock.Text = $"{DownloadedCount} {FindResource("Of")} {Maximum}";
        DownloadedVideosProgressBar.Value = DownloadedCount;
    }

    public DownloadPage NyLoadFromSilent()
    {
        GlobalConsts.NyHideSettingsButton();
        GlobalConsts.NyHideAboutButton();
        GlobalConsts.NyHideHomeButton();
        GlobalConsts.NyHideHelpButton();
        return this;
    }

    private void NyExit_Click(object sender, RoutedEventArgs e)
    {
        SendToBackground();
    }
    public async void NyOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(Environment.GetEnvironmentVariable("WINDIR") + @"\explorer.exe", SavePath);
        }
        catch (Exception ex)
        {
            await GlobalConsts.NyLog($"Error opening save path: {ex}", "DownloadPage.xaml.cs at OpenFolder_Click");
        }
    }

    private async Task<bool> NyExitAsync()
    {
        CompletePersistedJob();
        cts?.Cancel(true);
        if (ffmpegList.Count > 0)
        {
            var yesno = await GlobalConsts.NyShowYesNoDialog($"{FindResource("StillConverting")}", $"{FindResource("StillConverting")} {ffmpegList.Count} {FindResource("files")} {FindResource("AreYouSureExit")}");
            if (yesno == MessageDialogResult.Negative)
                return false;
        }
        ffmpegList?.ForEach(x => { try { x.Kill(); } catch { } });
        StillDownloading = false;
        if (!silent)
            GlobalConsts.NyLoadPage(GlobalConsts.MainPage.NyLoad());

        return true;
    }

    public Task<bool> NyCancel()
    {
        if (!disposedValue)
            return NyExitAsync();

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
                cts?.Cancel(true);
                cts?.Dispose();
                try
                {
                    ffmpegList?.ForEach(x => { try { x.Kill(); } catch { } });
                    ffmpegList?.Clear();
                }
                catch { }
                NotDownloaded?.Clear();
                downloadSpeeds?.Clear();
            }

            StillDownloading = false;
            Playlist = null;
            ffmpegList = null;
            disposedValue = true;
            Videos = null;
            FileType = null;
            VideoSaveFormat = null;
            Bitrate = null;
            NotDownloaded = null;
            Videos = null;
            downloadSpeeds = null;
            title = null;
            currentTitle = null;
            currentStatus = null;
            totalDownloaded = null;
            currentDownloadSpeed = null;
            PropertyChanged = null;
        }
    }
    public void Dispose()
    {
        NyDispose(true);
    }
    #endregion
}

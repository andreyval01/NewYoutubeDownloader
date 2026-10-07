namespace YoutubePlaylistDownloader;

static class GlobalConsts
{
    #region Const Variables
    public static Skeleton Current;
    public static MainPage MainPage;
    public static System.Windows.Media.Brush ErrorBrush;
    public static readonly string TempFolderPath;
    //public static string SaveDirectory;
    public static readonly string CurrentDir;
    public static readonly string FFmpegFilePath;
    private static readonly string ConfigFilePath;
    private static readonly string ErrorFilePath;
    public static readonly Version VERSION = new(0, 1);
    public static bool UpdateOnExit;
    public static string UpdateSetupLocation;
    public static bool UpdateFinishedDownloading;
    public static bool UpdateLater;
    public static DownloadUpdate UpdateControl;
    public static readonly string ChannelSubscriptionsFilePath;
    public static TimeSpan SubscriptionsUpdateDelay;
    private static DownloadSettings downloadSettings;
    public static readonly string DownloadSettingsFilePath;
    public static readonly ObservableCollection<QueuedDownload> Downloads;
    private static SemaphoreSlim conversionLocker;
    public static Objects.Settings settings;

    public static string OppositeTheme => settings.Theme == "Light" ? "Dark" : "Light";
    public static YoutubeClient YoutubeClient => new();
    public static SemaphoreSlim ConversionsLocker { get => conversionLocker; set => conversionLocker ??= value; }
    public static DownloadSettings DownloadSettings
    {
        get
        {
            downloadSettings ??= new DownloadSettings("mp3", false, YoutubeHelpers.High720, false, false, false, false, "192", false, "en", true, false, 0, 0, false, true, false, true, 4, "$title", false, "mkv", "default");
            return downloadSettings;
        }
        set
        {
            if (value != null)
            {
                downloadSettings = value;
                DownloadGate.Limit = DownloadSettings.NormalizeSimultaneousDownloads(downloadSettings.MaxSimultaneousDownloads);
                File.WriteAllText(DownloadSettingsFilePath, JsonConvert.SerializeObject(downloadSettings));
            }
        }
    }

    #endregion

    static GlobalConsts()
    {
        JsonConvert.DefaultSettings = () =>
        {
            var settings = new JsonSerializerSettings();
            settings.Converters.Add(new VideoQualityConverter());
            return settings;
        };
        Downloads = [];
        DownloadGate.PriorityOf = id =>
        {
            if (string.IsNullOrWhiteSpace(id))
                return int.MaxValue;
            for (var i = 0; i < Downloads.Count; i++)
            {
                if (string.Equals(Downloads[i].JobId, id, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return int.MaxValue;
        };
        CurrentDir = new FileInfo(Assembly.GetEntryAssembly().Location).Directory.ToString();
        FFmpegFilePath = $"{CurrentDir}\\ffmpeg.exe";
        var appDataPath = AppPaths.AppDataDirectory + "\\";
        ConfigFilePath = string.Concat(appDataPath, "Settings.json");
        ErrorFilePath = string.Concat(appDataPath, "Errors.txt");
        DownloadSettingsFilePath = string.Concat(appDataPath, "DownloadSettings.json");
        ChannelSubscriptionsFilePath = string.Concat(appDataPath, "Subscriptions.ypds");

        if (!Directory.Exists(appDataPath))
            Directory.CreateDirectory(appDataPath);

        ErrorBrush = Brushes.Crimson;
        settings = new()
        {
            Language = "English"
        };
        TempFolderPath = AppPaths.TempDirectory + "\\";
        UpdateOnExit = false;
        UpdateLater = false;
        UpdateSetupLocation = string.Empty;
        SubscriptionsUpdateDelay = TimeSpan.FromMinutes(1);
        Downloads.CollectionChanged += NyDownloads_CollectionChanged;
    }

    //The const methods are used mainly for saving/loading consts, and handling page\menu management.
    #region Const Methods

    #region Buttons
    public static void NyHideHelpButton()
    {
        Current.HelpButton.Visibility = Visibility.Collapsed;
    }
    public static void NyHideHomeButton()
    {
        Current.HomeButton.Visibility = Visibility.Collapsed;
    }
    public static void NyHideAboutButton()
    {
    }
    public static void NyHideSettingsButton()
    {
        Current.SettingsButton.Visibility = Visibility.Collapsed;
    }
    public static void NyShowSettingsButton()
    {
        Current.SettingsButton.Visibility = Visibility.Visible;
    }
    public static void NyShowHelpButton()
    {
        Current.HelpButton.Visibility = Visibility.Visible;
    }
    public static void NyShowAboutButton()
    {
    }
    public static void NyShowHomeButton()
    {
        Current.HomeButton.Visibility = Visibility.Visible;
    }
    #endregion

    public static Task NyShowMessage(string title, string message)
    {
        if (!Current.Dispatcher.CheckAccess())
            return Current.Dispatcher.InvokeAsync(() => NyShowMessage(title, message)).Task.Unwrap();

        if (Current.DefaultFlyout.IsOpen)
            Current.DefaultFlyout.IsOpen = false;
        return Current.NyShowMessage(title, message);
    }
    public static async Task<MessageDialogResult> NyShowYesNoDialog(string title, string message)
    {
        if (Current.DefaultFlyout.IsOpen)
            Current.DefaultFlyout.IsOpen = false;
        return await Current.NyShowYesNoDialog(title, message).ConfigureAwait(false);
    }
    public static Task NyShowSelectableDialog(string title, string message, Action retryAction)
    {
        if (Current.DefaultFlyout.IsOpen)
            Current.DefaultFlyout.IsOpen = false;
        return Current.NyShowSelectableDialog(title, message, retryAction);
    }
    public static void NyLoadPage(UserControl page) => Current.CurrentPage.Content = page;
    public static void NySaveConsts()
    {
        try
        {
            File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(settings));
            NySaveDownloadSettings();
            DownloadQueueStore.NyFlush();
        }
        catch (Exception ex)
        {
            NyLog(ex.ToString(), "SaveConsts").Wait();
        }
    }
    public static void NyRestoreDefualts()
    {
        NyLog("Restoring defaults", "RestoreDefaults at GlobalConsts").Wait();
        settings = new Objects.Settings("Dark", "Red", "English", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), false, false, true, TimeSpan.FromMinutes(1), true, 20, 2, true, true);
        DownloadSettings = new DownloadSettings("mp3", false, YoutubeHelpers.High720, false, false, false, false, "192", false, "en", true, false, 0, 0, false, true, false, true, 4, "$title", false, "mkv", "default");
        NySaveConsts();
    }
    public static void NyLoadConsts()
    {

        if (!File.Exists(ConfigFilePath))
        {
            NyLog("Config file does not exist, restoring defaults", "LoadConsts at GlobalConsts").Wait();

            NyRestoreDefualts();
            return;
        }

        try
        {
            settings = JsonConvert.DeserializeObject<Objects.Settings>(File.ReadAllText(ConfigFilePath));
            ConversionsLocker = new SemaphoreSlim(settings.ActualConversionsLimit, settings.MaximumConversionsCount);

            NyLoadDownloadSettings();
        }
        catch (Exception ex)
        {
            NyLog(ex.ToString(), "LoadConsts at GlobalConsts").Wait();
            NyRestoreDefualts();
        }
        settings.Language = AppLanguage.FromSystem();
        NyUpdateTheme();
        NyUpdateLanguage();
        NySaveConsts();

    }
    public static void NyCreateTempFolder()
    {
        try
        {
            if (!Directory.Exists(AppPaths.TempDirectory))
                Directory.CreateDirectory(AppPaths.TempDirectory);
        }
        catch (Exception ex)
        {
            NyLog($"Failed to create temp folder, {ex}", "CreateTempFolder at GlobalConsts").Wait();
        }

    }
    public static void NyCleanTempFolder()
    {
        if (Directory.Exists(AppPaths.TempDirectory))
        {
            DirectoryInfo di = new(AppPaths.TempDirectory);

            foreach (var file in di.GetFiles())
                try { file.Delete(); } catch { };

            foreach (var dir in di.GetDirectories())
                try { dir.Delete(true); } catch { };
        }
    }
    private static void NyUpdateTheme()
    {
        try
        {
            ThemeManager.Current.ChangeTheme(Application.Current, $"{OppositeTheme}.{settings.Accent}");
            ThemeManager.Current.ChangeTheme(Application.Current, $"{settings.Theme}.{settings.Accent}");
        }
        catch (Exception ex)
        {
            NyRestoreDefualts();
            NyLog(ex.ToString(), "UpdateTheme").ConfigureAwait(false);
        }
    }
    private static void NyUpdateLanguage() => NyChangeLanguage(settings.Language);

    public static void NyChangeLanguage(string nLang)
    {
        if (string.IsNullOrWhiteSpace(nLang))
            nLang = "English";

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        foreach (var dictionary in dictionaries.Where(IsAppLanguageDictionary).ToList())
            dictionaries.Remove(dictionary);

        dictionaries.Add(LoadLanguage("English"));
        if (!string.Equals(nLang, "English", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                dictionaries.Add(LoadLanguage(nLang));
            }
            catch (Exception ex)
            {
                NyLog(ex.ToString(), "ChangeLanguage").Wait();
                nLang = "English";
            }
        }

        settings.Language = nLang;
    }

    private static bool IsAppLanguageDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return !string.IsNullOrWhiteSpace(source)
            && source.Contains("/Languages/", StringComparison.OrdinalIgnoreCase)
            && !source.Contains("LanguagesList", StringComparison.OrdinalIgnoreCase);
    }

    private static ResourceDictionary LoadLanguage(string name) => new()
    {
        Source = new Uri($"/Languages/{name}.xaml", UriKind.Relative)
    };
    public static async Task NyLog(string message, object sender)
    {
        using StreamWriter sw = new(ErrorFilePath, true);
        await sw.WriteLineAsync($"[{DateTime.Now.ToUniversalTime()}], [{sender}]:\n\n{message}\n\n").ConfigureAwait(false);

    }
    public static string NyCleanFileName(string filename)
    {
        var invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
        var invalidReStr = string.Format(@"[{0}]+", invalidChars);

        var reservedWords = new[]
        {
            "CON", "PRN", "AUX", "CLOCK$", "NUL", "COM0", "COM1", "COM2", "COM3", "COM4",
            "COM5", "COM6", "COM7", "COM8", "COM9", "LPT0", "LPT1", "LPT2", "LPT3", "LPT4",
            "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        var sanitisedNamePart = Regex.Replace(filename, invalidReStr, "_");
        foreach (var reservedWord in reservedWords)
        {
            var reservedWordPattern = string.Format("^{0}\\.", reservedWord);
            sanitisedNamePart = Regex.Replace(sanitisedNamePart, reservedWordPattern, "_reservedWord_.", RegexOptions.IgnoreCase);
        }

        return sanitisedNamePart;
    }

    static void NyCropAndSaveImage(byte[] imageBytes, string imagePath)
    {
        using var imageBuffer = new MemoryStream(imageBytes);
        using var image = System.Drawing.Image.FromStream(imageBuffer);
        var cropRectangle = new Rectangle((image.Width - image.Height) / 2, 0, image.Height, image.Height);
        using var bitmap = new Bitmap(cropRectangle.Width, cropRectangle.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawImage(image, new Rectangle(0, 0, bitmap.Width, bitmap.Height), cropRectangle, GraphicsUnit.Pixel);
        bitmap.Save(imagePath, ImageFormat.Jpeg);
    }

    static readonly string[] ignoredComments = ["Auto-generated by YouTube.", "Provided to YouTube by"];
    internal static readonly string[] IgnoredGeneres = ["download", "out now", "mostercat", "video", "lyric", "release", "ncs", "records"];
    internal static readonly string[] ArtistsSeparators = ["&", "feat.", "feat", "ft.", " ft ", "Feat.", " x ", " X "];
    internal static readonly string[] VideoTitleSeparators = [" - ", " — "];

    static async Task<string> NyTagMusicFile(Video fullVideo, string file, int vIndex)
    {
        // Index YouTube Auto Generated Description
        var description = fullVideo.Description.Split("\n");
        var title = string.Empty;
        var artists = new List<string>();
        var album = string.Empty;
        var releaseDate = default(DateTime);
        var comment = new StringBuilder();
        var commentIndex = 0;

        try
        {
            foreach (var line in description)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    goto loopEnd;
                }

                if (line.Contains('·'))
                {
                    var titleAndArtists = line.Split('·').Select(x => x.Trim());

                    title = titleAndArtists.FirstOrDefault();
                    artists.AddRange(titleAndArtists.Skip(1));
                    album = description.ElementAtOrDefault(commentIndex + 2);

                    if (album != null && !album.Contains("Auto-generated by YouTube.") && !album.StartsWith("Released on:"))
                    {
                        goto loopEnd;
                    }
                    else
                    {
                        album = string.Empty;
                    }
                }

                if (line.StartsWith("Released on:"))
                {
                    releaseDate = DateTime.Parse(line.Split(":").ElementAtOrDefault(1));
                    goto loopEnd;
                }

                if (!string.IsNullOrWhiteSpace(album) && line.StartsWith(album))
                {
                    goto loopEnd;
                }

                if (ignoredComments.Any(x => line.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
                {
                    goto loopEnd;
                }

                comment.AppendLine(line);

            loopEnd:
                commentIndex++;
            }
        }
        catch (Exception e)
        {
            await NyLog(e.ToString(), "TagMusicFile inside description loop").ConfigureAwait(false);
            return null;
        }

        if (releaseDate == default)
        {
            releaseDate = fullVideo.UploadDate.Date;
        }

        using (var tagLibFile = TagLib.File.Create(file))
        {
            tagLibFile.Tag.Title = title;
            tagLibFile.Tag.Performers = [.. artists];
            tagLibFile.Tag.Year = (uint)releaseDate.Year;
            tagLibFile.Tag.Comment = comment.ToString();
            tagLibFile.Tag.Album = album;
            tagLibFile.Tag.Track = (uint)vIndex;

            try
            {
                var picturePath = $"{TempFolderPath}{NyCleanFileName(fullVideo.Title)}.jpg";

                using (var httpClient = new HttpClient())
                {
                    var pictureContent = await httpClient.GetByteArrayAsync($"https://img.youtube.com/vi/{fullVideo.Id}/maxresdefault.jpg").ConfigureAwait(false);
                    NyCropAndSaveImage(pictureContent, picturePath);
                }

                tagLibFile.Tag.Pictures = [new TagLib.Picture(picturePath)];
            }
            catch (Exception ex)
            {
                await NyLog("Failed to add picture to file at TagMusicFile", ex.ToString()).ConfigureAwait(false);
            }

            tagLibFile.Save();
        }

        return $"{string.Join(", ", artists)} - {title}";
    }

    public static async Task<string> NyTagFileBasedOnTitle(IVideo video, int index, string file, FullPlaylist playlist = null)
    {
        var title = video.Title.Replace("—", "-");
        var genre = title.Split('[', ']').ElementAtOrDefault(1);

        if (genre == null)
        {
            genre = string.Empty;
        }
        else if (genre.Length >= title.Length)
        {
            genre = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(genre))
        {
            title = title.Replace($"[{genre}]", string.Empty);
            var stringToRemove = title.Split('[', ']', '【', '】').ElementAtOrDefault(1);

            if (!string.IsNullOrWhiteSpace(stringToRemove))
            {
                title = title.Replace($"[{stringToRemove}]", string.Empty);
            }
        }

        title = title.TrimStart(' ', '-', '[', ']').TrimEnd();

        using (var tagLibFile = TagLib.File.Create(file))
        {
            tagLibFile.Tag.Album = playlist?.BasePlaylist?.Title;
            tagLibFile.Tag.Track = (uint)index;
            tagLibFile.Tag.AlbumArtists = [playlist?.BasePlaylist?.Author?.ChannelTitle];

            if (IgnoredGeneres.Any(genre.ToLower().Contains))
            {
                genre = string.Empty;
            }
            else
            {
                tagLibFile.Tag.Genres = genre.Split('/', '\\');
            }

            if (NyTryGetSongTitleAndPerformersFromTitle(title, out string songTitle, out string[] songPerformers))
            {
                tagLibFile.Tag.Title = songTitle;
                tagLibFile.Tag.Performers = songPerformers;
            }

            try
            {
                var picturePath = $"{TempFolderPath}{NyCleanFileName(video.Title)}.jpg";
                var pictureUrl = video.Thumbnails?.TryGetWithHighestResolution()?.Url
                    ?? $"https://img.youtube.com/vi/{video.Id}/maxresdefault.jpg";

                using (var httpClient = new HttpClient())
                {
                    var response = await httpClient.GetAsync(pictureUrl).ConfigureAwait(false);

                    using (var pictureStream = File.Create(picturePath))
                    {
                        await response.Content.CopyToAsync(pictureStream).ConfigureAwait(false);
                    }
                }

                tagLibFile.Tag.Pictures = [new TagLib.Picture(picturePath)];
            }
            catch (Exception ex)
            {
                await NyLog("Failed to add picture to file at TagFileBasedOnTitle", ex.ToString()).ConfigureAwait(false);
            }

            tagLibFile.Save();
        }

        return file;
    }

    public static bool NyTryGetSongTitleAndPerformersFromTitle(string title, out string songTitle, out string[] songPerformers)
    {
        songTitle = null;
        songPerformers = null;

        var index = title.LastIndexOf('-');

        if (index > 0)
        {
            songTitle = title[(index + 1)..].Trim(' ', '-');

            if (string.IsNullOrWhiteSpace(songTitle))
            {
                index = title.IndexOf('-');

                if (index > 0)
                {
                    songTitle = title[(index + 1)..].Trim(' ', '-');
                }
            }

            songPerformers = title[..(index - 1)].Trim().Split(ArtistsSeparators, StringSplitOptions.RemoveEmptyEntries);

            return true;
        }

        return false;
    }

    public static async Task<string> NyTagFile(IVideo video, int vIndex, string file, FullPlaylist playlist = null)
    {
        ArgumentNullException.ThrowIfNull(video);

        if (video is GenericVideo)
        {
            return await NyTagFileBasedOnTitle(video, vIndex, file, playlist);
        }

        if (!VideoTitleSeparators.Any(video.Title.Contains))
        {
            var fullVideo = await YoutubeClient.Videos.GetAsync(video.Id).ConfigureAwait(false);

            if (fullVideo.Description.Contains("Auto-generated by YouTube."))
            {
                var fileName = await NyTagMusicFile(fullVideo, file, vIndex);

                if (fileName != null)
                {
                    return fileName;
                }
            }
        }

        return await NyTagFileBasedOnTitle(video, vIndex, file, playlist);
    }

    public static void NyLoadFlyoutPage(UserControl page)
    {
        Current.DefaultFlyoutUserControl.Content = page;
        Current.DefaultFlyout.IsOpen = true;
    }

    public static void NyCloseFlyout()
    {
        Current.DefaultFlyout.IsOpen = false;
        Current.DefaultFlyoutUserControl.Content = null;
    }

    public static double NyGetOffset()
    {
        return Current.ActualHeight - 95;
    }

    private static void NyLoadDownloadSettings()
    {
        if (File.Exists(DownloadSettingsFilePath))
        {
            try
            {
                downloadSettings = JsonConvert.DeserializeObject<DownloadSettings>(File.ReadAllText(DownloadSettingsFilePath));
                if (downloadSettings != null)
                    DownloadGate.Limit = DownloadSettings.NormalizeSimultaneousDownloads(downloadSettings.MaxSimultaneousDownloads);
            }
            catch (Exception ex)
            {
                NyLog(ex.ToString(), "LoadDownloadSettings at GlobalConsts").Wait();
                try
                {
                    if (File.Exists(DownloadSettingsFilePath))
                        File.Delete(DownloadSettingsFilePath);
                }
                catch (Exception ex2)
                {
                    NyLog(ex2.ToString(), "Delete download settings file path").Wait();
                }
                downloadSettings = new DownloadSettings("mp3", false, YoutubeHelpers.High720, false, false, false, false, "192", false, "en", true, false, 0, 0, false, true, false, true, 4, "$title", false, "mkv", "default");
            }
        }
        else
        {
            downloadSettings = new DownloadSettings("mp3", false, YoutubeHelpers.High720, false, false, false, false, "192", false, "en", true, false, 0, 0, false, true, false, true, 4, "$title", false, "mkv", "default");
        }
    }

    public static void NySaveDownloadSettings()
    {
        try
        {
            File.WriteAllText(DownloadSettingsFilePath, JsonConvert.SerializeObject(downloadSettings));
        }
        catch (Exception ex)
        {
            NyLog(ex.ToString(), "SaveDownloadSettings at GlobalConsts").Wait();
        }
    }

    public static void MoveDownload(QueuedDownload item, int delta) => MoveDownloads([item], delta);

    public static void MoveDownloadToTop(QueuedDownload item) => MoveDownloadsToEdge([item], toTop: true);

    public static void MoveDownloads(IReadOnlyList<QueuedDownload> items, int delta)
    {
        var ordered = OrderedDownloads(items);
        if (ordered.Count == 0 || delta == 0)
            return;
        if (delta < 0)
        {
            if (Downloads.IndexOf(ordered[0]) == 0)
                return;
            foreach (var item in ordered)
                MoveOne(item, -1);
        }
        else
        {
            if (Downloads.IndexOf(ordered[^1]) == Downloads.Count - 1)
                return;
            for (var i = ordered.Count - 1; i >= 0; i--)
                MoveOne(ordered[i], 1);
        }

        PersistDownloadOrder();
    }

    public static void MoveDownloadsToEdge(IReadOnlyList<QueuedDownload> items, bool toTop)
    {
        var ordered = OrderedDownloads(items);
        if (ordered.Count == 0)
            return;
        if (toTop)
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                var index = Downloads.IndexOf(ordered[i]);
                if (index != i)
                    Downloads.Move(index, i);
            }
        }
        else
        {
            for (var i = ordered.Count - 1; i >= 0; i--)
            {
                var index = Downloads.IndexOf(ordered[i]);
                var target = Downloads.Count - (ordered.Count - i);
                if (index != target)
                    Downloads.Move(index, target);
            }
        }

        PersistDownloadOrder();
    }

    private static List<QueuedDownload> OrderedDownloads(IReadOnlyList<QueuedDownload> items)
    {
        if (items == null || items.Count == 0)
            return [];
        return items.Where(item => item != null && Downloads.Contains(item))
            .Distinct()
            .OrderBy(Downloads.IndexOf)
            .ToList();
    }

    private static void MoveOne(QueuedDownload item, int delta)
    {
        var index = Downloads.IndexOf(item);
        if (index < 0)
            return;
        var target = Math.Clamp(index + delta, 0, Downloads.Count - 1);
        if (target == index)
            return;
        Downloads.Move(index, target);
    }

    private static void PersistDownloadOrder()
    {
        DownloadGate.NotifyQueueChanged();
        DownloadQueueStore.SetOrder(Downloads.Select(x => x.JobId).Where(x => !string.IsNullOrWhiteSpace(x)).ToList());
    }

    private static void NyDownloads_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Remove)
            foreach (QueuedDownload item in e.OldItems)
                item?.Dispose();
    }

    #endregion
}

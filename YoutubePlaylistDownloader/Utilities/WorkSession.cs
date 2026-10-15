using System.Globalization;

namespace YoutubePlaylistDownloader.Utilities;

internal static class WorkSession
{
    static bool suspendRefresh;
    static int startupGate;

    public static bool Settled { get; private set; }

    public static void Refresh()
    {
        if (suspendRefresh)
            return;
        var downloads = CountOpenDownloads();
        var transcodes = CountRunningTranscodes();
        if (downloads + transcodes > 0)
            SessionStore.Write("running", "ask", downloads, transcodes);
        else
            SessionStore.Write("idle", "ask", 0, 0);
    }

    public static bool HasLiveWork()
    {
        var downloads = GlobalConsts.Downloads
            .Select(item => item.Item)
            .OfType<DownloadPage>()
            .Any(page => page.StillDownloading && !page.IsPaused);
        return downloads || TranscodePage.Instance?.IsEncoding == true;
    }

    public static async Task OnStartupAsync()
    {
        if (Interlocked.Exchange(ref startupGate, 1) != 0)
            return;
        DownloadPage.EnsureHooks();
        try
        {
            var session = SessionStore.Load();
            var downloads = CountOpenDownloads();
            var transcodes = TranscodeJobStore.CountAutoResume();
            if (session == null)
            {
                if (downloads + transcodes > 0)
                    await AskInterruptedAsync(downloads, transcodes).ConfigureAwait(true);
                else
                {
                    Settled = true;
                    await DownloadPage.ResumePersistedJobsAsync().ConfigureAwait(true);
                }
                return;
            }

            var state = session.State ?? "";
            var resume = session.Resume ?? "";
            if (state == "idle")
            {
                Settled = true;
                await DownloadPage.ResumePersistedJobsAsync().ConfigureAwait(true);
                return;
            }

            if (state == "closed" && resume == "no")
            {
                Settled = true;
                DownloadPage.ShowSavedQueue(markUnpausedAsPaused: true);
                TranscodePage.Instance?.ShowUnfinishedJobs();
                return;
            }

            if (state == "closed" && resume == "yes")
            {
                await ContinueSavedAsync().ConfigureAwait(true);
                return;
            }

            if (state == "running")
            {
                if (downloads + transcodes == 0)
                {
                    Settled = true;
                    await DownloadPage.ResumePersistedJobsAsync().ConfigureAwait(true);
                    return;
                }

                await AskInterruptedAsync(downloads, transcodes).ConfigureAwait(true);
            }
        }
        finally
        {
            Settled = true;
            TranscodePage.Instance?.AllowProbe();
            Application.Current?.Dispatcher.BeginInvoke(
                () => GlobalConsts.MainPage?.PlaceQueueOnActiveJob(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    public static async Task<bool?> ConfirmCloseAsync()
    {
        if (!HasLiveWork())
            return null;

        suspendRefresh = true;
        var exiting = false;
        try
        {
            DownloadPage.BeginExitHoldAll();
            var transcodeHeld = TranscodePage.Instance?.BeginExitHold() == true;
            DownloadQueueStore.NyFlush();
            var downloads = CountOpenDownloads();
            var transcodes = TranscodeJobStore.CountAutoResume();
            SessionStore.Write("running", "ask", downloads, transcodes);
            var parts = Describe(downloads, transcodes, capitalizeFirst: false);
            var message = string.Format((string)Application.Current.FindResource("SessionExitQuestion"), parts);
            var answer = await GlobalConsts.Current.NyShowThreeButtons(
                (string)Application.Current.FindResource("Exit"),
                message,
                (string)Application.Current.FindResource("SessionContinueLater"),
                (string)Application.Current.FindResource("SessionDontExit"),
                (string)Application.Current.FindResource("SessionDontContinue")).ConfigureAwait(true);

            if (answer == MessageDialogResult.Affirmative)
            {
                SessionStore.Write("closed", "yes", downloads, transcodes);
                exiting = true;
                return true;
            }

            if (answer == MessageDialogResult.FirstAuxiliary)
            {
                DownloadPage.CommitExitHoldPaused();
                TranscodePage.Instance?.LeavePausedAfterExit();
                DownloadQueueStore.NyFlush();
                SessionStore.Write("closed", "no", 0, 0);
                exiting = true;
                return true;
            }

            DownloadPage.CancelExitHoldAll();
            if (transcodeHeld)
                TranscodePage.Instance?.CancelExitHold();
            return false;
        }
        finally
        {
            if (!exiting)
            {
                suspendRefresh = false;
                Refresh();
            }
        }
    }

    static async Task AskInterruptedAsync(int downloads, int transcodes)
    {
        var parts = Describe(downloads, transcodes, capitalizeFirst: true);
        var message = string.Format((string)Application.Current.FindResource("SessionCrashQuestion"), parts);
        var answer = await GlobalConsts.Current.NyShowThreeButtons(
            (string)Application.Current.FindResource("Title"),
            message,
            (string)Application.Current.FindResource("SessionContinue"),
            (string)Application.Current.FindResource("SessionNotNow"),
            null).ConfigureAwait(true);
        if (answer == MessageDialogResult.Affirmative)
        {
            SessionStore.Write("closed", "yes", downloads, transcodes);
            await ContinueSavedAsync().ConfigureAwait(true);
            return;
        }

        DownloadPage.ShowSavedQueue(markUnpausedAsPaused: true);
        TranscodePage.Instance?.QuiesceInterrupted();
        SessionStore.Write("closed", "no", 0, 0);
    }

    static async Task ContinueSavedAsync()
    {
        if (TranscodePage.Instance != null)
            await TranscodePage.Instance.AskResumeQuestionsAsync().ConfigureAwait(true);
        Settled = true;
        var downloads = DownloadPage.ResumePersistedJobsAsync();
        var transcodes = TranscodePage.Instance?.RunReadyJobsAsync() ?? Task.CompletedTask;
        await Task.WhenAll(downloads, transcodes).ConfigureAwait(true);
        Refresh();
    }

    public static int CountOpenDownloads()
    {
        DownloadQueueSnapshot snapshot;
        try
        {
            snapshot = DownloadQueueStore.GetSnapshot();
        }
        catch
        {
            return 0;
        }

        var jobs = snapshot.Jobs ?? [];
        var count = jobs.Count(job => job != null && !job.Paused);
        foreach (var item in snapshot.Pending ?? [])
        {
            if (string.IsNullOrWhiteSpace(item?.Url))
                continue;
            if (jobs.Any(job => string.Equals(job.Url, item.Url, StringComparison.OrdinalIgnoreCase)))
                continue;
            count++;
        }

        return count;
    }

    static int CountRunningTranscodes()
    {
        return TranscodeJobStore.List().Count(job =>
            job != null
            && !job.UserPaused
            && job.State is "queued" or "running"
            && TranscodeJobStore.CanResume(job));
    }

    static string Describe(int downloads, int transcodes, bool capitalizeFirst)
    {
        var parts = new List<string>();
        if (downloads > 0)
            parts.Add(string.Format((string)Application.Current.FindResource("SessionDownloadsPart"), downloads));
        if (transcodes > 0)
            parts.Add(string.Format((string)Application.Current.FindResource("SessionTranscodesPart"), transcodes));
        if (capitalizeFirst && parts.Count > 0)
            parts[0] = Capitalize(parts[0]);
        return string.Join(", ", parts);
    }

    static string Capitalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        return char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
    }
}

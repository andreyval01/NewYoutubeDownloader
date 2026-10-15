namespace YoutubePlaylistDownloader;

public partial class TranscodePage
{
    sealed class JobChoice
    {
        public string Id { get; init; }
        public string Title { get; init; }
    }

    public static TranscodePage Instance { get; private set; }
    public bool IsEncoding => runningJob && !batchPaused;

    bool batchPaused;
    int pauseWave;
    bool allowProbe;
    bool heldForExit;
    bool suppressJobEvent = true;
    TranscodeJob liveJob;
    TaskCompletionSource<bool> resumeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    DateTime nextProgressWriteUtc;
    readonly Dictionary<string, string> resumeDecision = new(StringComparer.OrdinalIgnoreCase);
    string viewedJobId;
    List<TranscodeFileRow> draftFiles;

    public void AllowProbe()
    {
        allowProbe = true;
        if (viewedJobId != null && !runningJob)
            _ = ProbeVisibleAsync();
    }

    public void ShowUnfinishedJobs()
    {
        RefreshJobBox(preferUnfinished: true);
        RefreshEstimates();
    }

    public bool BeginExitHold()
    {
        if (!runningJob || batchPaused || liveJob == null)
            return false;
        heldForExit = true;
        PauseBatch(byUser: false);
        return true;
    }

    public void CancelExitHold()
    {
        if (!heldForExit)
            return;
        heldForExit = false;
        if (runningJob && batchPaused)
            ReleasePause();
    }

    public void LeavePausedAfterExit()
    {
        if (liveJob == null)
            return;
        liveJob.State = "paused";
        if (heldForExit)
            liveJob.UserPaused = false;
        RememberJob(liveJob, force: true);
        heldForExit = false;
    }

    public void QuiesceInterrupted()
    {
        var missing = (string)FindResource("TranscodeSourceMissing");
        foreach (var job in TranscodeJobStore.List())
        {
            if (job.State is "completed" or "cancelled")
                continue;
            TranscodeJobStore.Reconcile(job, missing);
            TranscodeJobStore.Upsert(job);
        }

        RefreshJobBox(preferUnfinished: true);
    }

    public async Task AskResumeQuestionsAsync()
    {
        resumeDecision.Clear();
        var missing = (string)FindResource("TranscodeSourceMissing");
        foreach (var job in TranscodeJobStore.List().Where(TranscodeJobStore.IsAutoResume))
        {
            TranscodeJobStore.Reconcile(job, missing);
            if (!TranscodeJobStore.CanResume(job))
            {
                TranscodeJobStore.Upsert(job);
                continue;
            }

            var decision = await DecideAsync(job, includeFinished: AgainBox.IsChecked == true).ConfigureAwait(true);
            if (decision == "cancel")
            {
                job.State = "paused";
                job.UserPaused = false;
                TranscodeJobStore.Upsert(job);
                resumeDecision[job.Id] = "cancel";
                continue;
            }

            ApplyDecision(job, decision);
            job.State = "paused";
            TranscodeJobStore.Upsert(job);
            resumeDecision[job.Id] = decision;
        }

        RefreshJobBox(preferUnfinished: true);
    }

    public async Task RunReadyJobsAsync()
    {
        var ids = TranscodeJobStore.List().Where(TranscodeJobStore.IsAutoResume).Select(job => job.Id).ToList();
        foreach (var id in ids)
        {
            if (resumeDecision.TryGetValue(id, out var decision) && decision == "cancel")
                continue;
            var job = TranscodeJobStore.List().FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (job == null || !TranscodeJobStore.CanResume(job))
                continue;
            var force = decision == "redo" || AgainBox.IsChecked == true;
            await RunJobAsync(job, force).ConfigureAwait(true);
        }
    }

    async void Start_Click(object sender, RoutedEventArgs e) => await StartNewBatchAsync();

    async void Pause_Click(object sender, RoutedEventArgs e) => await PauseOrContinueAsync();

    void Cancel_Click(object sender, RoutedEventArgs e) => CancelBatch();

    async Task StartNewBatchAsync()
    {
        if (!WorkSession.Settled || runningJob)
            return;
        EnsureDraft();
        var preset = SelectedPreset();
        if (preset == null || preset.KeepOriginal)
            return;
        var batch = Scope().Where(file => file.Facts?.ProbeOk == true && preset.AppliesTo(file.Facts)).ToList();
        if (batch.Count == 0)
            return;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow.ToString("o");
        var job = new TranscodeJob
        {
            Id = Guid.NewGuid().ToString("D"),
            CreatedUtc = now,
            UpdatedUtc = now,
            Preset = TranscodeJobStore.FromPreset(preset, preset.Title),
            OutputDirectory = outputDirectory ?? "",
            State = "paused",
            UserPaused = false,
            Files = batch.Select(row =>
            {
                var output = UniqueOutput(TranscodePlanner.OutputPath(row.Path, preset, row.Facts, outputDirectory), used);
                return new TranscodeJobFile
                {
                    Source = row.Path,
                    Output = output,
                    Signature = TranscodePlanner.MarkSignature(preset, row.Facts),
                    State = "pending",
                    Progress = 0
                };
            }).ToList()
        };

        var decision = await DecideAsync(job, includeFinished: AgainBox.IsChecked == true).ConfigureAwait(true);
        if (decision == "cancel")
            return;
        ApplyDecision(job, decision);
        TranscodeJobStore.Upsert(job);
        RefreshJobBox(selectId: job.Id);
        if (!job.Files.Any(file => file.State is "pending" or "paused" or "failed" or "working"))
        {
            job.State = "completed";
            TranscodeJobStore.Upsert(job);
            ApplyJobStatuses(job);
            WorkSession.Refresh();
            return;
        }

        await RunJobAsync(job, decision == "redo" || AgainBox.IsChecked == true).ConfigureAwait(true);
    }

    async Task PauseOrContinueAsync()
    {
        if (!WorkSession.Settled)
            return;
        if (runningJob && !batchPaused)
        {
            PauseBatch(byUser: true);
            return;
        }

        if (runningJob && batchPaused)
        {
            var decision = await DecideAsync(liveJob, includeFinished: AgainBox.IsChecked == true).ConfigureAwait(true);
            if (decision == "cancel")
                return;
            ApplyDecision(liveJob, decision);
            RememberJob(liveJob, force: true);
            if (decision == "redo" || AgainBox.IsChecked == true)
                forceRun = true;
            ReleasePause();
            return;
        }

        var job = ViewedJob();
        if (job == null || !TranscodeJobStore.CanResume(job))
            return;
        var missing = (string)FindResource("TranscodeSourceMissing");
        TranscodeJobStore.Reconcile(job, missing);
        var chosen = await DecideAsync(job, includeFinished: AgainBox.IsChecked == true).ConfigureAwait(true);
        if (chosen == "cancel")
        {
            job.State = "paused";
            TranscodeJobStore.Upsert(job);
            return;
        }

        ApplyDecision(job, chosen);
        TranscodeJobStore.Upsert(job);
        await RunJobAsync(job, chosen == "redo" || AgainBox.IsChecked == true).ConfigureAwait(true);
    }

    bool forceRun;

    void PauseBatch(bool byUser)
    {
        if (liveJob == null)
            return;
        batchPaused = true;
        pauseWave++;
        liveJob.UserPaused = byUser;
        liveJob.State = "paused";
        foreach (var file in liveJob.Files)
        {
            if (file.State != "working")
                continue;
            file.State = "paused";
            DeleteTemp(file.Output);
        }

        RememberJob(liveJob, force: true);
        List<Process> snapshot;
        lock (runningLock)
            snapshot = running.ToList();
        foreach (var process in snapshot)
            TryKill(process);
        ApplyJobStatuses(liveJob);
        UpdateStartButton();
        WorkSession.Refresh();
    }

    void ReleasePause()
    {
        if (liveJob != null)
        {
            liveJob.UserPaused = false;
            liveJob.State = "running";
            RememberJob(liveJob, force: true);
        }

        batchPaused = false;
        var gate = resumeGate;
        resumeGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.TrySetResult(true);
        UpdateStartButton();
        WorkSession.Refresh();
    }

    void CancelBatch()
    {
        try { runCts?.Cancel(); } catch { }
        batchPaused = false;
        resumeGate.TrySetResult(true);
        List<Process> snapshot;
        lock (runningLock)
            snapshot = running.ToList();
        foreach (var process in snapshot)
            TryKill(process);
        if (liveJob != null)
        {
            foreach (var file in liveJob.Files)
            {
                if (file.State is "pending" or "working" or "paused")
                {
                    file.State = "cancelled";
                    DeleteTemp(file.Output);
                }
            }

            liveJob.State = "cancelled";
            liveJob.UserPaused = false;
            RememberJob(liveJob, force: true);
            ApplyJobStatuses(liveJob);
        }
    }

    async Task RunJobAsync(TranscodeJob job, bool force)
    {
        if (runningJob || job == null)
            return;
        BindJobRows(job);
        liveJob = job;
        forceRun = force;
        if (resumeGate.Task.IsCompleted)
            resumeGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preset = TranscodeJobStore.ToPreset(job.Preset);
        runningJob = true;
        batchPaused = false;
        heldForExit = false;
        job.UserPaused = false;
        job.State = "running";
        RememberJob(job, force: true);
        var runnable = job.Files.Where(file => file.State is "pending" or "paused" or "failed" or "working").ToList();
        BeginProgress(Math.Max(1, runnable.Count));
        UpdateStartButton();
        WorkSession.Refresh();
        runCts = new CancellationTokenSource();
        var token = runCts.Token;
        var limit = DownloadSettings.NormalizeSimultaneousDownloads(GlobalConsts.DownloadSettings?.MaxSimultaneousDownloads ?? 2);
        using var gate = new SemaphoreSlim(limit);
        try
        {
            var tasks = runnable.Select(file => EncodeFileAsync(file, preset, gate, token));
            await Task.WhenAll(tasks).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (token.IsCancellationRequested)
            {
                foreach (var file in job.Files)
                {
                    if (file.State is "pending" or "working" or "paused")
                        file.State = "cancelled";
                }

                job.State = "cancelled";
                job.UserPaused = false;
            }
            else if (job.Files.All(file => file.State is "done" or "skipped" or "cancelled"))
            {
                job.State = job.Files.Any(file => file.State is "done" or "skipped") && job.Files.All(file => file.State is "done" or "skipped" or "cancelled")
                    && job.Files.Any(file => file.State != "cancelled")
                    ? "completed"
                    : job.Files.All(file => file.State == "cancelled") ? "cancelled" : "completed";
            }
            else
            {
                job.State = "paused";
                job.UserPaused = false;
            }

            RememberJob(job, force: true);
            runningJob = false;
            batchPaused = false;
            heldForExit = false;
            ApplyJobStatuses(job);
            UpdateStartButton();
            RefreshEstimates();
            RefreshJobBox(selectId: job.Id);
            WorkSession.Refresh();
        }
    }

    async Task EncodeFileAsync(TranscodeJobFile file, TranscodePreset preset, SemaphoreSlim gate, CancellationToken token)
    {
        var row = files.FirstOrDefault(item => string.Equals(item.Path, file.Source, StringComparison.OrdinalIgnoreCase));
        while (!token.IsCancellationRequested)
        {
            await WaitUntilResumed(token).ConfigureAwait(false);
            if (token.IsCancellationRequested || file.State is "skipped" or "done" or "cancelled")
                return;
            await gate.WaitAsync(token).ConfigureAwait(false);
            Process process = null;
            var temp = "";
            var release = true;
            var wave = pauseWave;
            try
            {
                if (batchPaused)
                    continue;
                if (row == null)
                {
                    file.State = "failed";
                    file.Error = TrimError((string)FindResource("TranscodeSourceMissing"));
                    RememberJob(liveJob, force: true);
                    return;
                }

                if (!File.Exists(row.Path))
                {
                    file.State = "failed";
                    file.Error = TrimError((string)FindResource("TranscodeSourceMissing"));
                    await SetStatus(row, file.Error, null);
                    RememberJob(liveJob, force: true);
                    return;
                }

                if (row.Facts?.ProbeOk != true)
                {
                    var facts = await TranscodePlanner.ProbeAsync(row.Path, token).ConfigureAwait(false);
                    await Dispatcher.InvokeAsync(() => ApplyFacts(row, facts));
                }

                if (batchPaused)
                    continue;
                token.ThrowIfCancellationRequested();

                if (TranscodePlanner.IsStillDownloading(row.Path))
                {
                    file.State = "skipped";
                    file.Error = (string)FindResource("TranscodeBusyDownload");
                    file.Progress = 100;
                    await SetStatus(row, file.Error, 100);
                    RememberJob(liveJob, force: true);
                    return;
                }

                if (!forceRun && file.State != "failed" && TranscodePlanner.AlreadyDone(file.Output, file.Signature))
                {
                    file.State = "skipped";
                    file.Error = "";
                    file.Progress = 100;
                    file.OutputBytes = LengthOf(file.Output);
                    await SetStatus(row, (string)FindResource("TranscodeSkipped"), 100);
                    RememberJob(liveJob, force: true);
                    return;
                }

                if (SameFile(row.Path, file.Output)
                    && TranscodePlanner.IsCopyCodec(preset.VideoCodec)
                    && TranscodePlanner.IsCopyCodec(preset.AudioCodec)
                    && !preset.AudioOnly)
                {
                    file.State = "skipped";
                    file.Error = (string)FindResource("TranscodeUnchanged");
                    file.Progress = 100;
                    await SetStatus(row, file.Error, 100);
                    RememberJob(liveJob, force: true);
                    return;
                }

                if (batchPaused)
                    continue;
                var keptProgress = file.Progress;
                wave = pauseWave;
                var directory = Path.GetDirectoryName(file.Output);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                temp = file.Output + ".transcode.tmp";
                if (File.Exists(temp))
                    File.Delete(temp);
                file.State = "working";
                file.Error = "";
                file.Progress = 0;
                RememberJob(liveJob, force: true);
                await SetStatus(row, (string)FindResource("TranscodeWorking"), 0);
                if (batchPaused || pauseWave != wave || token.IsCancellationRequested)
                {
                    file.Progress = keptProgress;
                    file.State = batchPaused ? "paused" : "pending";
                    DeleteTemp(file.Output);
                    temp = "";
                    RememberJob(liveJob, force: true);
                    if (batchPaused && row != null)
                        await SetStatus(row, (string)FindResource("TranscodePaused"), keptProgress);
                    token.ThrowIfCancellationRequested();
                    continue;
                }

                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = GlobalConsts.FFmpegFilePath,
                        Arguments = TranscodePlanner.BuildArguments(row.Facts, preset, temp),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    },
                    EnableRaisingEvents = true
                };
                var errors = new StringBuilder();
                process.OutputDataReceived += (_, args) => ReportFileProgress(row, file, args.Data);
                process.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                        errors.AppendLine(args.Data);
                };
                lock (runningLock)
                    running.Add(process);
                if (batchPaused || pauseWave != wave || token.IsCancellationRequested)
                {
                    file.Progress = keptProgress;
                    file.State = batchPaused ? "paused" : "pending";
                    DeleteTemp(file.Output);
                    temp = "";
                    RememberJob(liveJob, force: true);
                    if (batchPaused && row != null)
                        await SetStatus(row, (string)FindResource("TranscodePaused"), keptProgress);
                    token.ThrowIfCancellationRequested();
                    continue;
                }

                if (!process.Start())
                    throw new InvalidOperationException("ffmpeg did not start.");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (batchPaused || pauseWave != wave)
                    TryKill(process);
                await process.WaitForExitAsync(token).ConfigureAwait(false);
                if (!token.IsCancellationRequested && pauseWave != wave)
                {
                    DeleteTemp(file.Output);
                    temp = "";
                    file.State = batchPaused ? "paused" : "pending";
                    if (batchPaused && row != null)
                        await SetStatus(row, (string)FindResource("TranscodePaused"), null);
                    RememberJob(liveJob, force: true);
                    continue;
                }

                if (process.ExitCode != 0)
                    throw new InvalidOperationException(errors.ToString().Trim());
                TranscodePlanner.Commit(row.Path, temp, file.Output);
                TranscodePlanner.WriteMark(file.Output, file.Signature);
                temp = "";
                file.State = "done";
                file.Error = "";
                file.Progress = 100;
                file.OutputBytes = LengthOf(file.Output);
                await SetStatus(row, (string)FindResource("TranscodeDone"), 100);
                RememberJob(liveJob, force: true);
                return;
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                file.State = "cancelled";
                DeleteTemp(file.Output);
                temp = "";
                if (row != null)
                    await SetStatus(row, (string)FindResource("TranscodeCancel"), null);
                RememberJob(liveJob, force: true);
                return;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                {
                    TryKill(process);
                    file.State = "cancelled";
                    DeleteTemp(file.Output);
                    temp = "";
                    if (row != null)
                        await SetStatus(row, (string)FindResource("TranscodeCancel"), null);
                    RememberJob(liveJob, force: true);
                    return;
                }

                if (pauseWave != wave)
                {
                    DeleteTemp(file.Output);
                    temp = "";
                    file.State = batchPaused ? "paused" : "pending";
                    if (batchPaused && row != null)
                        await SetStatus(row, (string)FindResource("TranscodePaused"), null);
                    RememberJob(liveJob, force: true);
                    continue;
                }

                TryKill(process);
                file.State = "failed";
                file.Error = TrimError(ex.Message);
                file.Progress = row?.Progress ?? file.Progress;
                if (row != null)
                    await SetStatus(row, file.Error, null);
                RememberJob(liveJob, force: true);
                return;
            }
            finally
            {
                if (process != null)
                {
                    lock (runningLock)
                        running.Remove(process);
                    process.Dispose();
                }

                if (!string.IsNullOrWhiteSpace(temp) && File.Exists(temp))
                {
                    try { File.Delete(temp); } catch { }
                }

                if (release)
                    gate.Release();
            }
        }
    }

    async Task WaitUntilResumed(CancellationToken token)
    {
        while (batchPaused && !token.IsCancellationRequested)
        {
            var gate = resumeGate;
            try
            {
                await gate.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
            }
        }

        token.ThrowIfCancellationRequested();
    }

    void ReportFileProgress(TranscodeFileRow row, TranscodeJobFile file, string line)
    {
        if (string.IsNullOrWhiteSpace(line) || row?.Facts == null)
            return;
        if (!TryProgressPercent(line.Trim(), row.Facts.Duration, out var percent))
            return;
        file.Progress = percent;
        if (file.State == "pending")
            file.State = "working";
        RememberJob(liveJob, force: false);

        _ = Dispatcher.InvokeAsync(() =>
        {
            row.Status = percent + "%";
            row.Progress = percent;
            NoteFileProgress(row, percent, row.Name);
        });
    }

    async Task<string> DecideAsync(TranscodeJob job, bool includeFinished)
    {
        if (job == null || runningJob)
            return "go";
        var considered = Considered(job, includeFinished).ToList();
        var same = 0;
        var different = 0;
        foreach (var file in considered)
        {
            switch (Classify(file))
            {
                case "same":
                    same++;
                    break;
                case "different":
                    different++;
                    break;
            }
        }

        if (same + different == 0)
            return "go";
        if (AgainBox.IsChecked == true)
            return "redo";
        var total = job.Files.Count;
        var message = string.Format((string)FindResource("TranscodeExistingQuestion"), same + different, total);
        if (same > 0 && different > 0)
        {
            message += Environment.NewLine + string.Format((string)FindResource("TranscodeExistingSame"), same);
            message += Environment.NewLine + string.Format((string)FindResource("TranscodeExistingDifferent"), different);
        }
        var answer = await GlobalConsts.Current.NyShowThreeButtons(
            (string)FindResource("Transcode"),
            message,
            (string)FindResource("TranscodeSkip"),
            (string)FindResource("TranscodeCancel"),
            (string)FindResource("TranscodeRedo")).ConfigureAwait(true);
        if (answer == MessageDialogResult.Affirmative)
            return "skip";
        if (answer == MessageDialogResult.FirstAuxiliary)
            return "redo";
        return "cancel";
    }

    static IEnumerable<TranscodeJobFile> Considered(TranscodeJob job, bool includeFinished)
    {
        foreach (var file in job.Files ?? [])
        {
            if (file == null || file.State == "cancelled")
                continue;
            if (includeFinished || file.State is "pending" or "paused" or "failed" or "working")
                yield return file;
        }
    }

    void ApplyDecision(TranscodeJob job, string decision)
    {
        var includeFinished = decision == "redo" && AgainBox.IsChecked == true;
        if (decision == "redo")
        {
            foreach (var file in Considered(job, includeFinished || AgainBox.IsChecked == true))
            {
                if (file.State is "done" or "skipped" or "paused" or "failed" or "working")
                    file.State = "pending";
            }

            return;
        }

        if (decision != "skip")
            return;
        foreach (var file in Considered(job, false))
        {
            var kind = Classify(file);
            if (kind is not ("same" or "different"))
                continue;
            file.State = "skipped";
            file.Error = "";
            file.Progress = 100;
            file.OutputBytes = LengthOf(file.Output);
        }
    }

    static string Classify(TranscodeJobFile file)
    {
        if (file == null || string.IsNullOrWhiteSpace(file.Output))
            return "none";
        if (TranscodePlanner.AlreadyDone(file.Output, file.Signature))
            return "same";
        return LengthOf(file.Output) > 0 ? "different" : "none";
    }

    void BindJobRows(TranscodeJob job)
    {
        if (job == null)
            return;
        if (viewedJobId == null)
            draftFiles = files.ToList();
        viewedJobId = job.Id;
        var existing = files.ToDictionary(row => row.Path, StringComparer.OrdinalIgnoreCase);
        var same = job.Files.Count == files.Count && job.Files.All(file => existing.ContainsKey(file.Source ?? ""));
        if (!same)
        {
            files.Clear();
            foreach (var file in job.Files)
            {
                var row = new TranscodeFileRow(file.Source ?? "")
                {
                    Progress = file.Progress,
                    Status = StatusText(file),
                    Selected = true
                };
                if (File.Exists(row.Path))
                    row.SizeText = TranscodePlanner.FormatSize(new FileInfo(row.Path).Length);
                row.PropertyChanged += FileRow_PropertyChanged;
                files.Add(row);
            }
        }
        else
        {
            ApplyJobStatuses(job);
        }

        ShowSnapshot(job.Preset);
        if (!string.IsNullOrWhiteSpace(job.OutputDirectory))
        {
            outputDirectory = job.OutputDirectory;
            OutputFolderText.Text = outputDirectory;
            OutputDefaultButton.Visibility = Visibility.Visible;
        }
    }

    void ApplyJobStatuses(TranscodeJob job)
    {
        if (job == null)
            return;
        foreach (var file in job.Files)
        {
            var row = files.FirstOrDefault(item => string.Equals(item.Path, file.Source, StringComparison.OrdinalIgnoreCase));
            if (row == null)
                continue;
            row.Status = StatusText(file);
            row.Progress = file.Progress;
        }
    }

    string StatusText(TranscodeJobFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.Error) && file.State is "failed" or "skipped")
            return file.Error;
        return file.State switch
        {
            "working" => (string)FindResource("TranscodeWorking"),
            "paused" => (string)FindResource("TranscodePaused"),
            "done" => (string)FindResource("TranscodeDone"),
            "skipped" => (string)FindResource("TranscodeSkipped"),
            "failed" => (string)FindResource("Error"),
            "cancelled" => (string)FindResource("TranscodeCancel"),
            _ => (string)FindResource("FileQueued")
        };
    }

    void ShowSnapshot(TranscodePresetSnapshot snapshot)
    {
        if (snapshot == null)
            return;
        var preset = TranscodeJobStore.ToPreset(snapshot);
        suppressCustomEvent = true;
        try
        {
            var match = presets.FirstOrDefault(item => item.Preset != null
                                                        && string.Equals(item.Preset.Id, snapshot.Id, StringComparison.OrdinalIgnoreCase)
                                                        && SameSettings(item.Preset, preset));
            if (match != null)
            {
                PresetGrid.SelectedItem = match;
                LoadControls(match.Preset);
            }
            else
            {
                var custom = presets.FirstOrDefault(item => item.Preset?.Id == "custom");
                if (custom != null)
                {
                    custom.Preset = new TranscodePreset
                    {
                        Id = "custom",
                        Title = custom.Title,
                        AudioOnly = preset.AudioOnly,
                        VideoCodec = preset.VideoCodec,
                        Crf = preset.Crf,
                        MaxHeight = preset.MaxHeight,
                        AudioCodec = preset.AudioCodec,
                        AudioBitrateKbps = preset.AudioBitrateKbps,
                        Container = preset.Container
                    };
                    custom.Note = Describe(custom.Preset);
                    PresetGrid.SelectedItem = custom;
                    LoadControls(custom.Preset);
                }
            }
        }
        finally
        {
            suppressCustomEvent = false;
        }

        UpdatePresetButtons();
    }

    void ReloadPresets(string selectId = null)
    {
        var keep = selectId ?? SelectedPreset()?.Id;
        suppressCustomEvent = true;
        try
        {
            presets.Clear();
            foreach (var preset in TranscodePlanner.BuiltIn(key => (string)FindResource(key)))
                presets.Add(new TranscodePresetRow { Preset = preset, Title = preset.Title, Note = preset.Note });
            foreach (var user in TranscodePresetStore.Load().OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var preset = new TranscodePreset
                {
                    Id = user.Id,
                    Title = user.Name,
                    AudioOnly = user.AudioOnly,
                    VideoCodec = user.VideoCodec,
                    Crf = user.Crf,
                    MaxHeight = user.MaxHeight,
                    AudioCodec = user.AudioCodec,
                    AudioBitrateKbps = user.AudioBitrateKbps,
                    Container = user.Container
                };
                presets.Add(new TranscodePresetRow { Preset = preset, Title = user.Name, Note = Describe(preset) });
            }

            var selected = presets.FirstOrDefault(item => string.Equals(item.Preset?.Id, keep, StringComparison.OrdinalIgnoreCase));
            PresetGrid.SelectedItem = selected ?? (presets.Count > 1 ? presets[1] : presets.FirstOrDefault());
            LoadControls(SelectedPreset());
        }
        finally
        {
            suppressCustomEvent = false;
        }

        UpdatePresetButtons();
        RefreshEstimates();
    }

    void UpdatePresetButtons()
    {
        var preset = SelectedPreset();
        var user = IsUserPreset(preset);
        if (UpdatePresetButton != null)
            UpdatePresetButton.IsEnabled = user;
        if (DeletePresetButton != null)
            DeletePresetButton.IsEnabled = user;
        if (PresetNameBox != null && preset != null && !suppressCustomEvent)
            PresetNameBox.Text = preset.Title ?? "";
    }

    static bool IsUserPreset(TranscodePreset preset)
    {
        if (preset == null || preset.KeepOriginal || string.IsNullOrWhiteSpace(preset.Id))
            return false;
        if (preset.Id is "custom" or "as-is" or "compatible" or "smaller" or "strong" or "opus" or "mp3")
            return false;
        return Guid.TryParse(preset.Id, out _);
    }

    async void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedPreset();
        if (selected?.KeepOriginal == true)
        {
            await GlobalConsts.NyShowMessage((string)FindResource("Transcode"), (string)FindResource("TranscodeCannotSaveAsIs"));
            return;
        }

        var name = CleanName(PresetNameBox.Text);
        if (name.Length == 0)
        {
            await GlobalConsts.NyShowMessage((string)FindResource("Transcode"), (string)FindResource("TranscodePresetNameEmpty"));
            return;
        }

        var draft = ReadControls();
        var stored = TranscodePresetStore.Load().ToList();
        var same = stored.FirstOrDefault(item => string.Equals(item.Name?.Trim(), name, StringComparison.CurrentCultureIgnoreCase));
        if (same != null)
        {
            var replace = await GlobalConsts.NyShowYesNoDialog(
                (string)FindResource("Transcode"),
                string.Format((string)FindResource("TranscodePresetReplace"), name));
            if (replace != MessageDialogResult.Affirmative)
                return;
            CopyOnto(same, draft, name);
        }
        else
        {
            var created = new UserTranscodePreset { Id = Guid.NewGuid().ToString("D"), Name = name };
            CopyOnto(created, draft, name);
            stored.Add(created);
            same = created;
        }

        TranscodePresetStore.SaveAll(stored);
        ReloadPresets(same.Id);
    }

    void UpdatePreset_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedPreset();
        if (!IsUserPreset(selected))
            return;
        var stored = TranscodePresetStore.Load().ToList();
        var target = stored.FirstOrDefault(item => string.Equals(item.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
        if (target == null)
            return;
        CopyOnto(target, ReadControls(), target.Name);
        TranscodePresetStore.SaveAll(stored);
        ReloadPresets(target.Id);
    }

    async void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedPreset();
        if (!IsUserPreset(selected))
            return;
        var answer = await GlobalConsts.NyShowYesNoDialog(
            (string)FindResource("Transcode"),
            string.Format((string)FindResource("TranscodeDeletePresetConfirm"), selected.Title));
        if (answer != MessageDialogResult.Affirmative)
            return;
        var stored = TranscodePresetStore.Load()
            .Where(item => !string.Equals(item.Id, selected.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        TranscodePresetStore.SaveAll(stored);
        ReloadPresets("compatible");
    }

    static void CopyOnto(UserTranscodePreset target, TranscodePreset draft, string name)
    {
        target.Name = name;
        target.AudioOnly = draft.AudioOnly;
        target.VideoCodec = draft.VideoCodec;
        target.MaxHeight = draft.MaxHeight;
        target.Crf = draft.Crf;
        target.AudioCodec = draft.AudioCodec;
        target.AudioBitrateKbps = draft.AudioBitrateKbps;
        target.Container = draft.Container;
    }

    static string CleanName(string value)
    {
        var name = (value ?? "").Trim();
        return name.Length > 80 ? name[..80] : name;
    }

    void RefreshJobBox(bool preferUnfinished = false, string selectId = null)
    {
        if (JobBox == null)
            return;
        var jobs = TranscodeJobStore.List()
            .OrderByDescending(job => job.UpdatedUtc ?? job.CreatedUtc ?? "")
            .ToList();
        var choices = new List<JobChoice>
        {
            new() { Id = "", Title = (string)FindResource("TranscodeNewJob") }
        };
        foreach (var job in jobs)
        {
            var stamp = job.CreatedUtc ?? "";
            if (DateTime.TryParse(stamp, out var created))
                stamp = created.ToLocalTime().ToString("g");
            var name = job.Preset?.Name;
            if (string.IsNullOrWhiteSpace(name))
                name = (string)FindResource("Transcode");
            choices.Add(new JobChoice
            {
                Id = job.Id,
                Title = name + " · " + (job.Files?.Count ?? 0) + " · " + stamp + " · " + JobStateText(job)
            });
        }

        var wanted = selectId;
        if (wanted == null && JobBox.SelectedItem is JobChoice current)
            wanted = current.Id;
        if (preferUnfinished && string.IsNullOrEmpty(selectId))
        {
            var unfinished = jobs.FirstOrDefault(TranscodeJobStore.IsAutoResume)
                             ?? jobs.FirstOrDefault(job => job.UserPaused && TranscodeJobStore.CanResume(job))
                             ?? jobs.FirstOrDefault(TranscodeJobStore.CanResume);
            wanted = unfinished?.Id ?? "";
        }

        JobChoice selected = null;
        suppressJobEvent = true;
        try
        {
            JobBox.ItemsSource = choices;
            selected = choices.FirstOrDefault(item => string.Equals(item.Id, wanted, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
            JobBox.SelectedItem = selected;
        }
        finally
        {
            suppressJobEvent = false;
        }

        if (!runningJob && selected != null && !string.IsNullOrEmpty(selected.Id))
        {
            var job = jobs.FirstOrDefault(item => string.Equals(item.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
            if (job != null && !string.Equals(viewedJobId, job.Id, StringComparison.OrdinalIgnoreCase))
                BindJobRows(job);
        }
        else if (!runningJob && selected != null && string.IsNullOrEmpty(selected.Id) && viewedJobId != null)
        {
            EnsureDraft();
        }

        UpdateJobButtons();
    }

    string JobStateText(TranscodeJob job)
    {
        if (job.UserPaused || job.State == "paused")
            return (string)FindResource("TranscodePaused");
        return job.State switch
        {
            "running" => (string)FindResource("TranscodeWorking"),
            "completed" => (string)FindResource("TranscodeDone"),
            "cancelled" => (string)FindResource("TranscodeCancel"),
            _ => job.State ?? ""
        };
    }

    void JobBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressJobEvent || runningJob)
            return;
        if (JobBox.SelectedItem is not JobChoice choice)
            return;
        if (string.IsNullOrEmpty(choice.Id))
        {
            EnsureDraft();
            UpdateJobButtons();
            return;
        }

        var job = TranscodeJobStore.List().FirstOrDefault(item => string.Equals(item.Id, choice.Id, StringComparison.OrdinalIgnoreCase));
        if (job == null)
            return;
        BindJobRows(job);
        UpdateJobButtons();
        if (allowProbe)
            _ = ProbeVisibleAsync();
        else
            RefreshEstimates();
    }

    async void DeleteJob_Click(object sender, RoutedEventArgs e)
    {
        var job = ViewedJob();
        if (job == null || (runningJob && string.Equals(liveJob?.Id, job.Id, StringComparison.OrdinalIgnoreCase)))
            return;
        var answer = await GlobalConsts.NyShowYesNoDialog(
            (string)FindResource("Transcode"),
            (string)FindResource("TranscodeDeleteJobConfirm"));
        if (answer != MessageDialogResult.Affirmative)
            return;
        TranscodeJobStore.Remove(job.Id);
        if (string.Equals(viewedJobId, job.Id, StringComparison.OrdinalIgnoreCase))
        {
            viewedJobId = null;
            EnsureDraft();
        }

        RefreshJobBox(preferUnfinished: false, selectId: "");
    }

    TranscodeJob ViewedJob()
    {
        if (JobBox.SelectedItem is not JobChoice choice || string.IsNullOrEmpty(choice.Id))
            return null;
        return TranscodeJobStore.List().FirstOrDefault(item => string.Equals(item.Id, choice.Id, StringComparison.OrdinalIgnoreCase));
    }

    void EnsureDraft()
    {
        if (viewedJobId == null)
            return;
        viewedJobId = null;
        files.Clear();
        foreach (var row in draftFiles ?? [])
            files.Add(row);
        draftFiles = null;
        RefreshEstimates();
    }

    void UpdateJobButtons()
    {
        var job = ViewedJob();
        if (DeleteJobButton != null)
            DeleteJobButton.IsEnabled = job != null && !(runningJob && string.Equals(liveJob?.Id, job.Id, StringComparison.OrdinalIgnoreCase));
    }

    async Task ProbeVisibleAsync()
    {
        var pending = files.Where(file => file.Facts == null && File.Exists(file.Path)).ToList();
        using var gate = new SemaphoreSlim(4);
        var tasks = pending.Select(async row =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var facts = await TranscodePlanner.ProbeAsync(row.Path, CancellationToken.None).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() => ApplyFacts(row, facts));
            }
            catch
            {
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(RefreshEstimates);
    }

    void RememberJob(TranscodeJob job, bool force)
    {
        if (job == null)
            return;
        if (!force && DateTime.UtcNow < nextProgressWriteUtc && job.State == "running")
            return;
        if (job.State == "running" && !force)
            nextProgressWriteUtc = DateTime.UtcNow.AddMilliseconds(1500);
        TranscodeJobStore.Upsert(job);
    }

    static long LengthOf(string path)
    {
        try
        {
            var info = new FileInfo(path ?? "");
            return info.Exists ? info.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    static string TrimError(string message)
    {
        message = (message ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length > 180 ? message[..180] : message;
    }

    static void DeleteTemp(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;
        var temp = output + ".transcode.tmp";
        if (!File.Exists(temp))
            return;
        try { File.Delete(temp); } catch { }
    }
}

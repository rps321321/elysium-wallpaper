using ElysiumWallpaper.Services;

namespace ElysiumWallpaper.ViewModels;

/// <summary>
/// Engine loop, cycle orchestration, image resolution, and wallpaper application.
/// This is the side of the VM that actually pushes pixels to the desktop.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Rejects the current slot's image and pulls a fresh alternative from Pexels.
    /// In slot-based mode, operates on the current time-of-day slot. In interval mode,
    /// rerolls against the random library pool (just forces a re-apply to pick a new one).
    /// </summary>
    [RelayCommand]
    private async Task ShuffleCurrentSlotAsync()
    {
        EngineLog.Write("reroll: command invoked");
        try
        {
            // Interval mode: just trigger another cycle; random-pool picker will choose a different
            // file and the recent-history ring keeps it away from repeats.
            if (ParseIntervalMinutes(AutoChangeMode) is not null)
            {
                StatusHeadline = "Picking another...";
                StatusHeadline = "Rerolling from library pool...";
                EngineLog.Write("reroll: interval-mode cycle");
                await RunCycleAsync(forceFetch: false, forceApply: true, UserActionToken());
                StatusHeadline = "Picked another from library.";
                return;
            }

            string slotKey = TimeSlotService.GetSlotKey(
                TimeSlotService.GetSlotForTime(DateTime.Now, CurrentBoundaries));
            StatusHeadline = $"Finding another {slotKey} image...";
            StatusHeadline = $"Rerolling {slotKey}...";
            EngineLog.Write($"reroll: slot mode, slot={slotKey}");

            int minW = MinWidth > 0 ? (int)MinWidth : 1920;
            int minH = MinHeight > 0 ? (int)MinHeight : 1080;
            string? newFile = await SlotAwareFetchService.FetchSingleSlotAsync(
                SearchClient, EmbeddedPexelsApiKey, slotKey, _imagesPath, minW, minH, UserActionToken());

            SlotManifest.InvalidateCache(Path.Combine(_imagesPath, SlotManifest.FileName));
            if (newFile is null)
            {
                StatusHeadline = $"Could not find an alternative for {slotKey}.";
                EngineLog.Write($"reroll: no alternative found for {slotKey}");
                return;
            }

            // Force a fresh apply even if the filename didn't change (Windows caches by path).
            string? fullPath = ResolveCurrentImageAbsolutePath();
            if (fullPath is not null)
            {
                // Break the path cache by clearing CurrentImageFullPath first.
                await RunOnUiThreadAsync(() => CurrentImageFullPath = string.Empty);
                ApplyAndTrack(fullPath, originTag: "user");
            }
            StatusHeadline = $"Rerolled {slotKey} wallpaper.";
            EngineLog.Write($"reroll: applied {newFile}");
        }
        catch (Exception ex)
        {
            StatusHeadline = $"Reroll failed: {ex.Message}";
            EngineLog.Write($"reroll: exception {ex.GetType().Name}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StartEngineAsync()
    {
        if (IsEngineRunning) return;

        _engineCancellation = new CancellationTokenSource();
        IsEngineRunning = true;
        StatusHeadline = "Engine running";
        EngineLog.Write("engine started");

        try
        {
            await RunCycleAsync(forceFetch: true, forceApply: true, _engineCancellation.Token);
            _engineLoopTask = Task.Run(() => RunLoopAsync(_engineCancellation.Token));
        }
        catch (OperationCanceledException)
        {
            await ResetEngineStateAsync("Engine stopped");
        }
        catch (Exception ex)
        {
            EngineLog.Write($"engine initial cycle failed: {ex.Message}");
            await ResetEngineStateAsync($"Engine failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StopEngineAsync()
    {
        if (!IsEngineRunning) return;

        if (_engineCancellation is not null)
        {
            try { await _engineCancellation.CancelAsync(); } catch (Exception ex) { EngineLog.Write($"engine cancel: {ex.Message}"); }
        }

        if (_engineLoopTask is not null)
        {
            try { await _engineLoopTask; } catch { /* OCE or prior surfaced error */ }
        }

        await ResetEngineStateAsync("Engine stopped");
        EngineLog.Write("engine stopped");
    }

    /// <summary>Centralized reset so start-failure and stop paths can't leave half-state.</summary>
    private Task ResetEngineStateAsync(string statusHeadline) => RunOnUiThreadAsync(() =>
    {
        IsEngineRunning = false;
        StatusHeadline = statusHeadline;
        _engineLoopTask = null;
        var cts = _engineCancellation;
        _engineCancellation = null;
        try { cts?.Dispose(); } catch (Exception ex) { EngineLog.Write($"engine cts-dispose: {ex.Message}"); }
    });

    [RelayCommand]
    private Task FetchNowAsync() => RunCycleAsync(forceFetch: true, forceApply: true, UserActionToken());

    /// <summary>
    /// Token used by manually-triggered cycles. Piggy-backs on the engine's CTS when running so
    /// a Stop Engine cancels in-flight manual work too; otherwise returns a fresh None.
    /// </summary>
    private CancellationToken UserActionToken()
        => _engineCancellation?.Token ?? CancellationToken.None;

    /// <summary>#2: fetch one aspect-matched image per monitor and apply each to its target display.</summary>
    [RelayCommand]
    private async Task PerMonitorAutoAssignAsync()
    {
        if (SystemSpecs.Displays.Count == 0)
        {
            StatusHeadline = "No displays detected yet.";
            return;
        }

        string tag = string.IsNullOrWhiteSpace(SearchTag) ? "landscape" : SearchTag.Trim();
        StatusHeadline = $"Fetching one image per display for \"{tag}\"...";
        try
        {
            int maxParallel = Math.Max(1, Math.Min(4, Math.Max(1, SystemSpecs.Cpu.PhysicalCores / 2)));
            var map = await MonitorFetchService.FetchOneImagePerMonitorAsync(
                SearchClient,
                EmbeddedPexelsApiKey,
                tag,
                _libraryPath,
                SystemSpecs.Displays,
                CancellationToken.None,
                maxParallel);

            int applied = 0;
            foreach (var (monitorId, path) in map)
            {
                if (WallpaperService.Apply(path, SelectedLayoutMode, monitorId))
                {
                    AddHistory(path);
                    applied++;
                }
            }
            SaveProfile();
            StatusHeadline = applied == 0
                ? "Per-monitor fetch returned no usable images."
                : $"Applied {applied} wallpapers, one per monitor.";
        }
        catch (Exception ex)
        {
            StatusHeadline = $"Per-monitor auto-assign failed: {ex.Message}";
        }
    }

    partial void OnAutoChangeModeChanged(string value)
    {
        if (_isLoadingProfile) return;
        SaveProfile();
        EngineLog.Write($"auto-change mode set to '{value}'");
        _ = RunCycleAsync(forceFetch: false, forceApply: true, UserActionToken());
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // W2 - wake at the next slot boundary (or every N min in interval mode).
                var nextDelay = ComputeLoopDelay(DateTime.Now, AutoChangeMode, CurrentBoundaries);
                await Task.Delay(nextDelay, cancellationToken);
                // forceApply=true only for interval rotation; locked + slot-time modes rely on
                // the CurrentImageFullPath change-detection so they no-op when nothing changed.
                bool intervalMode = ParseIntervalMinutes(AutoChangeMode) is not null;
                await RunCycleAsync(forceFetch: false, forceApply: intervalMode, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop - swallow.
        }
        catch (Exception ex)
        {
            EngineLog.Write($"loop terminated unexpectedly: {ex}");
            await ResetEngineStateAsync($"Engine halted: {ex.Message}");
        }
    }

    /// <summary>
    /// W2 - compute next wake time. Slot mode: shorter of 15 min vs. next slot boundary.
    /// Interval mode: the user-chosen interval.
    /// </summary>
    internal static TimeSpan ComputeLoopDelay(DateTime now, string mode, SlotBoundaries? boundaries = null)
    {
        int? minutes = ParseIntervalMinutes(mode);
        if (minutes is int m)
        {
            return TimeSpan.FromMinutes(m);
        }

        DateTime next = TimeSlotService.NextBoundary(now, boundaries ?? SlotBoundaries.Default);
        TimeSpan toBoundary = next - now;
        return toBoundary < EngineMaxSleep ? toBoundary + TimeSpan.FromSeconds(2) : EngineMaxSleep;
    }

    /// <summary>Returns the interval in minutes when the mode string encodes one, else null.</summary>
    internal static int? ParseIntervalMinutes(string? mode) => AutoChangeModes.GetIntervalMinutes(mode ?? "");

    private async Task RunCycleAsync(bool forceFetch, bool forceApply, CancellationToken cancellationToken)
    {
        // C4 - one cycle at a time; manual Apply/Fetch + loop never overlap.
        await _cycleGate.WaitAsync(cancellationToken);
        bool calledByEngine = !forceApply || !forceFetch;  // loop calls pass both false
        try
        {
            if (forceFetch)
            {
                bool fetched = await RunFetchScriptAsync(cancellationToken);
                await RunOnUiThreadAsync(() =>
                    StatusHeadline = fetched ? "Fetched fresh wallpapers" : "Fetch failed, keeping local files");
            }

            string? fullPath = ResolveCurrentImageAbsolutePath();
            // W3 - compare on full path, not filename, so favorites-from-library vs images-folder don't collide.
            if (!string.IsNullOrWhiteSpace(fullPath)
                && (forceApply || !string.Equals(CurrentImageFullPath, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                await ApplyAndTrackAsync(fullPath, originTag: calledByEngine ? "engine" : "user");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EngineLog.Write($"cycle error: {ex.Message}");
            await RunOnUiThreadAsync(() =>
            {
                StatusHeadline = "Error while running wallpaper cycle";
                StatusHeadline = $"Error at {DateTime.Now:HH:mm:ss}: {ex.Message}";
            });
        }
        finally
        {
            _cycleGate.Release();
        }
    }

    /// <summary>Absolute path of the image the engine should apply this tick, or null if none found.</summary>
    private string? ResolveCurrentImageAbsolutePath()
    {
        if (!Directory.Exists(_imagesPath)) return null;

        // Interval mode: random from full pool (library + images), excluding recent history.
        // PURE: only reads the recent-history ring, doesn't mutate. Track call is done in
        // ApplyAndTrack once the apply actually happened.
        if (ParseIntervalMinutes(AutoChangeMode) is not null)
        {
            var pool = BuildWallpaperPool();
            if (pool.Count > 0)
            {
                List<string> available;
                lock (_recentlyAppliedLock)
                {
                    available = pool.Where(p => !_recentlyApplied.Contains(p)).ToList();
                    if (available.Count == 0)
                    {
                        _recentlyApplied.Clear();
                        available = pool;
                    }
                }
                return available[Random.Shared.Next(available.Count)];
            }
        }

        // Time-of-day: pick slot based on current time + user-configured boundaries.
        string slotKey = TimeSlotService.GetSlotKey(
            TimeSlotService.GetSlotForTime(DateTime.Now, CurrentBoundaries));

        var manifest = SlotManifest.LoadCached(Path.Combine(_imagesPath, SlotManifest.FileName));
        return SlotImageResolver.Resolve(slotKey, _imagesPath, manifest);
    }

    private Task ApplyAndTrackAsync(string path) => ApplyAndTrackAsync(path, originTag: "user");

    /// <summary>
    /// Applies the wallpaper and propagates the outcome to UI-bound state. Awaits the dispatcher
    /// so callers can reliably compare <see cref="CurrentImageFullPath"/> after this returns.
    /// </summary>
    private async Task ApplyAndTrackAsync(string path, string originTag)
    {
        string? resWarning = CheckResolutionMismatch(path);
        bool applied = WallpaperService.Apply(path, SelectedLayoutMode, SelectedMonitorId);

        await RunOnUiThreadAsync(() =>
        {
            CurrentImageFullPath = path;
            StatusHeadline = applied ? "Wallpaper updated" : "Wallpaper update failed";
            if (resWarning is not null) StatusHeadline = resWarning;
            if (applied)
            {
                AddHistory(path, originTag);
                SaveProfile();
            }
        });

        if (applied)
        {
            TrackRecentlyApplied(path);
        }
        EngineLog.Write($"apply origin={originTag} ok={applied} path={path}");
    }

    // Legacy sync shim retained for the few call sites that can't easily go async.
    private void ApplyAndTrack(string path) => _ = ApplyAndTrackAsync(path, "user");
    private void ApplyAndTrack(string path, string originTag) => _ = ApplyAndTrackAsync(path, originTag);

    private string? CheckResolutionMismatch(string path)
    {
        try
        {
            var target = GetTargetDisplay();
            if (target is null) return null;

            // W6 - use lightweight FileInfo/magic-bytes read; no BitmapDecoder blocking the thread.
            var (imgW, imgH) = ReadImageDimensionsFast(path);
            if (imgW == 0 || imgH == 0) return null;

            double wRatio = imgW / (double)target.Width;
            double hRatio = imgH / (double)target.Height;
            if (Math.Min(wRatio, hRatio) < LowResolutionRatioThreshold)
            {
                return $"Warning: image is {imgW}x{imgH}, target display is {target.Width}x{target.Height} - will look soft.";
            }
        }
        catch
        {
        }
        return null;
    }

    private Models.DisplayInfo? GetTargetDisplay()
    {
        if (string.Equals(SelectedMonitorId, WallpaperService.AllMonitors, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(SelectedMonitorId))
        {
            return SystemSpecs.Displays.FirstOrDefault(d => d.IsPrimary)
                   ?? SystemSpecs.Displays.FirstOrDefault();
        }
        return SystemSpecs.Displays.FirstOrDefault(d =>
            string.Equals(d.DeviceName, SelectedMonitorId, StringComparison.OrdinalIgnoreCase))
            ?? SystemSpecs.Displays.FirstOrDefault(d => d.IsPrimary);
    }

    /// <summary>
    /// Magic-bytes header parse for JPEG / PNG / BMP. Reads ~64 bytes, no decode, no async - safe
    /// on any thread. Returns (0, 0) if the format isn't recognized or the file is short.
    /// </summary>
    private static (int w, int h) ReadImageDimensionsFast(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> buf = stackalloc byte[64];
            int read = fs.Read(buf);
            if (read < 12) return (0, 0);

            // PNG: 89 50 4E 47 0D 0A 1A 0A, width at offset 16-19, height 20-23 (big-endian)
            if (buf[0] == 0x89 && buf[1] == 0x50 && buf[2] == 0x4E && buf[3] == 0x47 && read >= 24)
            {
                int w = (buf[16] << 24) | (buf[17] << 16) | (buf[18] << 8) | buf[19];
                int h = (buf[20] << 24) | (buf[21] << 16) | (buf[22] << 8) | buf[23];
                return (w, h);
            }

            // BMP: 42 4D, width at 18-21 (little-endian), height at 22-25
            if (buf[0] == 0x42 && buf[1] == 0x4D && read >= 26)
            {
                int w = buf[18] | (buf[19] << 8) | (buf[20] << 16) | (buf[21] << 24);
                int h = buf[22] | (buf[23] << 8) | (buf[24] << 16) | (buf[25] << 24);
                return (w, Math.Abs(h));
            }

            // JPEG: FF D8 ... need to walk markers to find SOF0/SOF2.
            if (buf[0] == 0xFF && buf[1] == 0xD8)
            {
                fs.Position = 2;
                Span<byte> hdr = stackalloc byte[9];
                while (true)
                {
                    int b1 = fs.ReadByte();
                    int b2 = fs.ReadByte();
                    if (b1 != 0xFF || b2 < 0) return (0, 0);
                    while (b2 == 0xFF) b2 = fs.ReadByte();
                    int len1 = fs.ReadByte();
                    int len2 = fs.ReadByte();
                    if (len1 < 0 || len2 < 0) return (0, 0);
                    int segLen = (len1 << 8) | len2;

                    if (b2 is 0xC0 or 0xC1 or 0xC2 or 0xC3)
                    {
                        if (fs.Read(hdr) < 5) return (0, 0);
                        int h = (hdr[1] << 8) | hdr[2];
                        int w = (hdr[3] << 8) | hdr[4];
                        return (w, h);
                    }

                    fs.Position += segLen - 2;
                    if (fs.Position >= fs.Length) return (0, 0);
                }
            }
        }
        catch
        {
        }
        return (0, 0);
    }

    /// <summary>
    /// Slot-aware native fetch: grabs one time-appropriate Pexels image per slot (sunrise/midday/
    /// sunset/starry) using the Pexels API directly. Replaces the legacy python-scraping path.
    /// </summary>
    private async Task<bool> RunFetchScriptAsync(CancellationToken cancellationToken)
    {
        try
        {
            int minW = MinWidth > 0 ? (int)MinWidth : 1920;
            int minH = MinHeight > 0 ? (int)MinHeight : 1080;
            bool ok = await SlotAwareFetchService.FetchAllSlotsAsync(
                SearchClient, EmbeddedPexelsApiKey, _imagesPath, minW, minH, cancellationToken);

            // Invalidate the manifest cache so the next resolve re-reads.
            SlotManifest.InvalidateCache(Path.Combine(_imagesPath, SlotManifest.FileName));
            EngineLog.Write($"slot-fetch complete ok={ok}");
            return ok;
        }
        catch (OperationCanceledException)
        {
            EngineLog.Write("slot-fetch cancelled");
            throw;
        }
        catch (Exception ex)
        {
            EngineLog.Write($"slot-fetch failed: {ex.Message}");
            return false;
        }
    }

    private List<string> BuildWallpaperPool()
    {
        var pool = new List<string>();
        AddFolderToPool(pool, _imagesPath);
        AddFolderToPool(pool, _libraryPath);
        // User-chosen folder is walked recursively so Dropbox/Pictures-style trees contribute.
        if (HasUserImagesFolder) AddFolderToPool(pool, UserImagesFolder, recursive: true);
        return pool;
    }

    private static void AddFolderToPool(List<string> pool, string folder, bool recursive = false)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var f in Directory.EnumerateFiles(folder, "*", option))
            {
                if (KnownExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    pool.Add(f);
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"pool-enum '{folder}' failed: {ex.Message}");
        }
    }

    private void TrackRecentlyApplied(string path)
    {
        lock (_recentlyAppliedLock)
        {
            _recentlyApplied.Enqueue(path);
            while (_recentlyApplied.Count > RecentHistoryWindow)
            {
                _recentlyApplied.Dequeue();
            }
        }
    }

    private bool WasRecentlyApplied(string path)
    {
        lock (_recentlyAppliedLock)
        {
            return _recentlyApplied.Contains(path);
        }
    }

    private void ClearRecentlyApplied()
    {
        lock (_recentlyAppliedLock) _recentlyApplied.Clear();
    }
}

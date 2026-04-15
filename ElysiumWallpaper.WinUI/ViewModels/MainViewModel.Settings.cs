using ElysiumWallpaper.Models;
using ElysiumWallpaper.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;

namespace ElysiumWallpaper.ViewModels;

/// <summary>
/// Settings, profile persistence, slot-boundary editing, system-spec-driven defaults,
/// export/import, and Windows startup registration.
/// </summary>
public sealed partial class MainViewModel
{
    [RelayCommand]
    private async Task RefreshSystemSpecsAsync()
    {
        if (IsLoadingSystemSpecs) return;
        IsLoadingSystemSpecs = true;
        try
        {
            SystemSpecs = await SystemInfoService.GetSpecsAsync();
            ApplySpecDrivenDefaults();
        }
        catch
        {
            // Silent - UI shows the default empty SystemSpecs placeholder.
        }
        finally
        {
            IsLoadingSystemSpecs = false;
        }
    }

    /// <summary>
    /// #10: computes a per-machine+primary-display profile filename and migrates legacy
    /// profile.json into it on first run, then re-loads so the UI reflects this machine's state.
    /// </summary>
    private void SwitchToMachineProfile()
    {
        var primary = SystemSpecs.Displays.FirstOrDefault(d => d.IsPrimary)
                      ?? SystemSpecs.Displays.FirstOrDefault();
        string displayKey = primary is null
            ? "no-display"
            : $"{primary.Width}x{primary.Height}";
        string machine = SanitizeForFilename(SystemSpecs.MachineName);
        string candidate = Path.Combine(_profileDir, $"profile-{machine}-{displayKey}.json");

        if (string.Equals(_profilePath, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _profilePath = candidate;

        if (!File.Exists(_profilePath) && File.Exists(_legacyProfilePath))
        {
            // First run on this machine - seed from legacy profile.json so nothing resets.
            File.Copy(_legacyProfilePath, _profilePath, overwrite: false);
        }

        LoadProfile();
    }

    private static string SanitizeForFilename(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "unknown";
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(s.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "unknown" : cleaned;
    }

    /// <summary>
    /// #1 + #4 + #9: auto-populate MinWidth/MinHeight from primary display, pick a smart
    /// default layout based on monitor count + arrangement, and flip the rich-effects flag
    /// on if the system has capable GPU VRAM.
    /// </summary>
    private void ApplySpecDrivenDefaults()
    {
        SwitchToMachineProfile();

        // Auto-set orientation filter based on primary display aspect (only if user left it on "Any").
        var primaryForOrientation = SystemSpecs.Displays.FirstOrDefault(d => d.IsPrimary)
                                    ?? SystemSpecs.Displays.FirstOrDefault();
        if (primaryForOrientation is not null && string.Equals(SelectedOrientation, "Any", StringComparison.OrdinalIgnoreCase))
        {
            double ratio = primaryForOrientation.Width / (double)Math.Max(1, primaryForOrientation.Height);
            SelectedOrientation = ratio switch
            {
                >= 1.2 => "landscape",
                <= 0.83 => "portrait",
                _ => "square"
            };
        }
        var primary = SystemSpecs.Displays.FirstOrDefault(d => d.IsPrimary)
                       ?? SystemSpecs.Displays.FirstOrDefault();

        // #1 - only fill in if user never set a value.
        if (primary is not null)
        {
            if (MinWidth <= 0) MinWidth = primary.Width;
            if (MinHeight <= 0) MinHeight = primary.Height;
        }

        // #4 - recommend layout based on topology; do NOT overwrite a user-selected value
        // other than the hard-coded "Fill" default used before specs loaded.
        string recommended = RecommendLayout();
        if (SelectedLayoutMode == "Fill" && !string.IsNullOrEmpty(recommended) && recommended != "Fill")
        {
            SelectedLayoutMode = recommended;
            StatusHeadline = $"Layout set to {recommended} based on your display setup.";
        }

        // #9 - detect 2+ GB VRAM for optional rich effects.
        SupportsRichEffects = SystemSpecs.Gpus.Any(g => g.AdapterRamBytes >= 2UL * 1024 * 1024 * 1024);
    }

    private string RecommendLayout()
    {
        var displays = SystemSpecs.Displays;
        if (displays.Count == 0) return "";
        if (displays.Count == 1)
        {
            var d = displays[0];
            return d.Height > d.Width ? "Fit" : "Fill";
        }

        // Detect horizontally-adjacent displays of matching resolution - Span wins there.
        var sortedByX = displays.OrderBy(d => d.PositionX).ToList();
        bool adjacent = true;
        for (int i = 1; i < sortedByX.Count; i++)
        {
            var a = sortedByX[i - 1];
            var b = sortedByX[i];
            if (Math.Abs((a.PositionX + a.Width) - b.PositionX) > 2 || a.Height != b.Height)
            {
                adjacent = false;
                break;
            }
        }
        return adjacent ? "Span" : "Fill";
    }

    /// <summary>
    /// Clears the user-chosen image folder. Picking a folder is async so the UI-facing flow lives
    /// in the code-behind (<see cref="Views.MainPage.PickUserFolder_Click"/>), which calls into
    /// <see cref="SetUserImagesFolder"/> with the resolved path.
    /// </summary>
    [RelayCommand]
    private void ClearUserImagesFolder()
    {
        UserImagesFolder = "";
        SaveProfile();
        StatusHeadline = "Local folder cleared.";
    }

    public void SetUserImagesFolder(string path)
    {
        UserImagesFolder = path ?? "";
        SaveProfile();
        StatusHeadline = string.IsNullOrWhiteSpace(path)
            ? "Local folder cleared."
            : $"Using local folder: {path}";
    }

    [RelayCommand]
    private void ExportProfile()
    {
        try
        {
            // Must be a synchronous write here - we need the file on disk before the copy.
            var snapshot = SnapshotProfile();
            Directory.CreateDirectory(Path.GetDirectoryName(_profilePath)!);
            File.WriteAllText(_profilePath, JsonSerializer.Serialize(snapshot, _jsonOptions));
            File.Copy(_profilePath, _profileExportPath, overwrite: true);
            StatusHeadline = $"Exported to {_profileExportPath}";
        }
        catch (Exception ex)
        {
            StatusHeadline = $"Export failed: {ex.Message}";
            EngineLog.Write($"ExportProfile failed: {ex}");
        }
    }

    [RelayCommand]
    private void ImportProfile()
    {
        if (!File.Exists(_profileExportPath))
        {
            StatusHeadline = "No export file found.";
            return;
        }

        File.Copy(_profileExportPath, _profilePath, overwrite: true);
        LoadProfile();
        StatusHeadline = "Profile imported.";
    }

    private void LoadProfile()
    {
        _isLoadingProfile = true;
        try
        {
            if (!File.Exists(_profilePath))
            {
                ApplyStartupState(false);
                SaveProfile();
                return;
            }

            var profile = JsonSerializer.Deserialize<AppProfile>(File.ReadAllText(_profilePath), _jsonOptions) ?? new AppProfile();
            SelectedLayoutMode = profile.LayoutMode;
            SelectedOrientation = profile.OrientationFilter;
            SelectedColorTone = profile.ColorToneFilter;
            MinWidth = profile.MinWidth;
            MinHeight = profile.MinHeight;
            IsTrayEnabled = profile.TrayEnabled;
            IsStartupEnabled = profile.StartupEnabled;
            SelectedMonitorId = profile.SelectedMonitorId;

            Favorites.Clear();
            foreach (var fav in profile.Favorites)
            {
                Favorites.Add(fav);
            }

            History.Clear();
            foreach (var item in profile.History)
            {
                History.Add(item);
            }

            Collections.Clear();
            foreach (var collection in profile.Collections)
            {
                Collections.Add(collection);
            }

            RecentTags.Clear();
            foreach (var tag in profile.RecentTags ?? [])
            {
                RecentTags.Add(tag);
            }

            SelectedTheme = string.IsNullOrWhiteSpace(profile.Theme) ? "Default" : profile.Theme;
            FavoritesFilter = profile.FavoritesFilter ?? "";
            FavoritesSort = string.IsNullOrWhiteSpace(profile.FavoritesSort) ? "Newest" : profile.FavoritesSort;
            AutoChangeMode = string.IsNullOrWhiteSpace(profile.AutoChangeMode) ? AutoChangeModes.TimeOfDay : profile.AutoChangeMode;
            UserImagesFolder = profile.UserImagesFolder ?? "";
            MorningStart = ParseTimeOrDefault(profile.MorningStart, new(6, 0, 0));
            NoonStart = ParseTimeOrDefault(profile.NoonStart, new(12, 0, 0));
            EveningStart = ParseTimeOrDefault(profile.EveningStart, new(17, 0, 0));
            NightStart = ParseTimeOrDefault(profile.NightStart, new(20, 0, 0));
            RelocateMissingImages();
            RebuildFilteredFavorites();

            ApplyStartupState(IsStartupEnabled);
            AppActions.SetTrayMode(IsTrayEnabled);
        }
        finally
        {
            _isLoadingProfile = false;
        }
    }

    /// <summary>
    /// Public entry-point used by all property-change handlers. Coalesces bursts of calls into
    /// a single background JSON write after <see cref="SaveDebounceMs"/> of quiet.
    /// </summary>
    private void SaveProfile()
    {
        try { _saveDebounceTimer.Change(SaveDebounceMs, System.Threading.Timeout.Infinite); }
        catch (Exception ex) { EngineLog.Write($"debounce-timer change: {ex.Message}"); }
    }

    /// <summary>
    /// Timer callback. Hops to the UI thread to snapshot mutable collections (ObservableCollection
    /// is NOT thread-safe to enumerate), then serializes + writes on a background task so disk I/O
    /// never blocks the UI.
    /// </summary>
    /// <summary>
    /// Last in-flight background write task. <see cref="Dispose"/> / <see cref="DisposeAsync"/>
    /// awaits this so process exit doesn't truncate the profile mid-write.
    /// </summary>
    private Task? _lastFlushTask;
    private readonly object _lastFlushLock = new();

    private void FlushProfileToDisk()
    {
        // Read on the timer thread is racy with LoadProfile flipping the flag on the UI
        // thread. Move the check inside TryEnqueue so it runs on the same thread that
        // mutates _isLoadingProfile, eliminating the race.
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_isLoadingProfile) return;

            AppProfile snapshot;
            try { snapshot = SnapshotProfile(); }
            catch (Exception ex) { EngineLog.Write($"profile snapshot failed: {ex.Message}"); return; }

            string targetPath = _profilePath;
            string directory = Path.GetDirectoryName(targetPath)!;
            var flush = Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(targetPath, JsonSerializer.Serialize(snapshot, _jsonOptions));
                }
                catch (Exception ex)
                {
                    EngineLog.Write($"profile write failed: {ex.Message}");
                }
            });
            lock (_lastFlushLock) { _lastFlushTask = flush; }
        });
    }

    /// <summary>
    /// Blocks (briefly) until the most recent background flush settles. Called from
    /// Dispose so a process-exit at the moment of the debounce timer firing doesn't
    /// produce a truncated profile.json.
    /// </summary>
    internal void WaitForLastFlush(TimeSpan timeout)
    {
        Task? t;
        lock (_lastFlushLock) { t = _lastFlushTask; }
        try { t?.Wait(timeout); }
        catch (Exception ex) { EngineLog.Write($"WaitForLastFlush: {ex.Message}"); }
    }

    private AppProfile SnapshotProfile() => new()
    {
        LayoutMode = SelectedLayoutMode,
        OrientationFilter = SelectedOrientation,
        ColorToneFilter = SelectedColorTone,
        MinWidth = (int)MinWidth,
        MinHeight = (int)MinHeight,
        TrayEnabled = IsTrayEnabled,
        StartupEnabled = IsStartupEnabled,
        SelectedMonitorId = SelectedMonitorId,
        Favorites = [.. Favorites],
        History = [.. History],
        Collections = [.. Collections],
        RecentTags = [.. RecentTags],
        Theme = SelectedTheme,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        WindowLeft = WindowLeft,
        WindowTop = WindowTop,
        FavoritesFilter = FavoritesFilter,
        FavoritesSort = FavoritesSort,
        AutoChangeMode = AutoChangeMode,
        UserImagesFolder = UserImagesFolder,
        MorningStart = MorningStart.ToString(@"hh\:mm"),
        NoonStart = NoonStart.ToString(@"hh\:mm"),
        EveningStart = EveningStart.ToString(@"hh\:mm"),
        NightStart = NightStart.ToString(@"hh\:mm")
    };

    partial void OnMorningStartChanged(TimeSpan value) { _boundariesCache = null; HandleBoundaryChange(); }
    partial void OnNoonStartChanged(TimeSpan value) { _boundariesCache = null; HandleBoundaryChange(); }
    partial void OnEveningStartChanged(TimeSpan value) { _boundariesCache = null; HandleBoundaryChange(); }
    partial void OnNightStartChanged(TimeSpan value) { _boundariesCache = null; HandleBoundaryChange(); }

    private void HandleBoundaryChange()
    {
        if (_isLoadingProfile) return;
        var proposed = new SlotBoundaries(MorningStart, NoonStart, EveningStart, NightStart);
        if (!proposed.IsValid)
        {
            StatusHeadline = "Slot times must be ordered morning < noon < evening < night.";
            return;
        }
        StatusHeadline = $"Slots: {MorningStart:hh\\:mm} / {NoonStart:hh\\:mm} / {EveningStart:hh\\:mm} / {NightStart:hh\\:mm}";
        SaveProfile();
        EngineLog.Write($"slot boundaries updated: {proposed}");
        _ = RunCycleAsync(forceFetch: false, forceApply: true, UserActionToken());
    }

    private SlotBoundaries CurrentBoundaries =>
        _boundariesCache ??= new SlotBoundaries(MorningStart, NoonStart, EveningStart, NightStart).OrDefault();

    private static TimeSpan ParseTimeOrDefault(string? raw, TimeSpan fallback)
    {
        if (TimeSpan.TryParseExact(raw, [@"hh\:mm", @"h\:mm", @"hh\:mm\:ss"], System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return fallback;
    }

    partial void OnSelectedLayoutModeChanged(string value) => SaveProfile();
    partial void OnSelectedOrientationChanged(string value) => SaveProfile();
    partial void OnSelectedColorToneChanged(string value) => SaveProfile();
    partial void OnMinWidthChanged(double value) => SaveProfile();
    partial void OnMinHeightChanged(double value) => SaveProfile();
    partial void OnSelectedMonitorIdChanged(string value) => SaveProfile();

    partial void OnIsTrayEnabledChanged(bool value)
    {
        SaveProfile();
    }

    partial void OnIsStartupEnabledChanged(bool value)
    {
        ApplyStartupState(value);
        SaveProfile();
    }

    private void ApplyStartupState(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            string exe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                key.SetValue("ElysiumWallpaper", $"\"{exe}\"");
            }
        }
        else
        {
            key.DeleteValue("ElysiumWallpaper", false);
        }
    }
}

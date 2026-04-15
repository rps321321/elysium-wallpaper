using ElysiumWallpaper.Models;
using ElysiumWallpaper.Services;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace ElysiumWallpaper.ViewModels;

/// <summary>
/// Root view-model for MainPage. Implementation is split across partial files by concern:
/// <list type="bullet">
///   <item><c>MainViewModel.Search.cs</c> - tag search, Pexels pagination, buffered filtering.</item>
///   <item><c>MainViewModel.Engine.cs</c> - engine loop, cycle orchestration, wallpaper apply.</item>
///   <item><c>MainViewModel.Library.cs</c> - favorites, history, collections, migration.</item>
///   <item><c>MainViewModel.Settings.cs</c> - profile persistence, slot boundaries, system specs.</item>
/// </list>
/// This file holds shared state (fields, observable properties), the constructor, and the
/// lifecycle plumbing (dispatcher hop, disposal) that every partial touches.
/// </summary>
public sealed partial class MainViewModel : BaseViewModel, IDisposable, IAsyncDisposable
{
    private const string EmbeddedPexelsApiKey = "[REDACTED-LEAKED-PEXELS-KEY]";
    private static readonly string[] KnownExtensions = [".jpg", ".jpeg", ".png", ".bmp"];
    private static readonly HttpClient SearchClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly string _appBasePath;
    private readonly string _imagesPath;
    private readonly string _libraryPath;
    private string _profilePath;
    private readonly string _profileExportPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly string _profileDir;
    private readonly string _legacyProfilePath;
    private CancellationTokenSource? _engineCancellation;
    private Task? _engineLoopTask;
    private bool _isLoadingProfile;
    private const int DisplayPageSize = 20;
    private const int PexelsFetchSize = 80;
    private const double LowResolutionRatioThreshold = 0.7;
    private static readonly TimeSpan EngineMaxSleep = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _cycleGate = new(1, 1);

    private readonly List<SearchResultViewModel> _filteredBuffer = [];
    private readonly HashSet<long> _seenPhotoIds = [];
    private string? _cachedSearchSignature;
    private int _pexelsPagesFetched;
    private int _pexelsTotalResults;

    private readonly Queue<string> _recentlyApplied = new();
    private readonly object _recentlyAppliedLock = new();
    private const int RecentHistoryWindow = 10;

    private readonly System.Threading.Timer _saveDebounceTimer;
    private const int SaveDebounceMs = 500;
    private SlotBoundaries? _boundariesCache;

    public MainViewModel(DispatcherQueue dispatcherQueue)
    {
        Title = "Elysium Wallpaper";
        _dispatcherQueue = dispatcherQueue;
        _saveDebounceTimer = new System.Threading.Timer(
            _ => { try { FlushProfileToDisk(); } catch (Exception ex) { EngineLog.Write($"SaveProfile flush failed: {ex.Message}"); } },
            null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _appBasePath = AppContext.BaseDirectory;
        _imagesPath = Path.Combine(_appBasePath, "images");

        _profileDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ElysiumWallpaper");
        Directory.CreateDirectory(_profileDir);

        // Library lives in LocalAppData so favorites survive clean rebuilds + exe moves.
        _libraryPath = Path.Combine(_profileDir, "library");
        Directory.CreateDirectory(_libraryPath);
        MigrateLegacyLibrary();

        _legacyProfilePath = Path.Combine(_profileDir, "profile.json");
        // #10 - machine+display-keyed profile path chosen once specs load; for now default to legacy.
        _profilePath = _legacyProfilePath;
        _profileExportPath = Path.Combine(_profileDir, "profile-export.json");

        SearchResults = [];
        Favorites = [];
        History = [];
        Collections = [];

        FilteredFavorites = [];
        PagedFavorites = new PagedView<FavoriteItem>(FilteredFavorites, pageSize: 12);
        PagedHistory = new PagedView<HistoryItem>(History, pageSize: 12);
        Favorites.CollectionChanged += (_, _) => RebuildFilteredFavorites();
        RecentTags = [];
        SessionStartedAt = DateTimeOffset.Now;
        Converters.TimestampIsRecentConverter.SessionThreshold = SessionStartedAt;

        LayoutModes = ["Fill", "Fit", "Stretch", "Center", "Span"];
        OrientationOptions = ["Any", "landscape", "portrait", "square"];
        ColorToneOptions = ["Any", "red", "orange", "yellow", "green", "turquoise", "blue", "violet", "pink", "brown", "black", "gray", "white"];

        // Initial values for ObservableProperty partial properties. Partial properties cannot
        // have field initializers in C# 13 — anything non-default(T) must be set here. LoadProfile
        // overwrites most of these from disk if a profile exists.
        //
        // Gate under _isLoadingProfile to prevent OnXChanged handlers (which call SaveProfile,
        // RunCycleAsync, RebuildFilteredFavorites, etc.) from firing during construction. The
        // old field-initializer style bypassed the setter; we have to suppress here instead.
        _isLoadingProfile = true;
        StatusHeadline = "Ready";
        CurrentImageFullPath = string.Empty;
        SearchTag = "nature";
        SearchStatus = "Search a tag to browse matching wallpapers.";
        SelectedLayoutMode = "Fill";
        SelectedOrientation = "Any";
        SelectedColorTone = "Any";
        SelectedMonitorId = WallpaperService.AllMonitors;
        NewCollectionName = string.Empty;
        SelectedCollectionName = string.Empty;
        SelectedSearchView = "Grid";
        SelectedLibraryTab = "Favorites";
        SearchPage = 1;
        SearchPerPage = DisplayPageSize;
        SearchPageLabel = string.Empty;
        SystemSpecs = new SystemSpecs();
        SelectedTheme = "Default";
        FavoritesFilter = string.Empty;
        FavoritesSort = "Newest";
        AutoChangeMode = AutoChangeModes.TimeOfDay;
        UserImagesFolder = string.Empty;
        MorningStart = new TimeSpan(6, 0, 0);
        NoonStart = new TimeSpan(12, 0, 0);
        EveningStart = new TimeSpan(17, 0, 0);
        NightStart = new TimeSpan(20, 0, 0);
        LastSearchError = string.Empty;
        MatchScreenAspect = true;
        AspectTolerance = 0.25;
        _isLoadingProfile = false;

        LoadProfile();
        DetectCurrentlyAppliedWallpaper();
        _ = RefreshSystemSpecsAsync();
    }

    /// <summary>
    /// Queries Windows for the wallpaper that is actually on the desktop right now and seeds
    /// <see cref="CurrentImage"/> / <see cref="CurrentImageFullPath"/> so the hero + backdrop
    /// render correctly on first launch, before the user has applied anything from the app.
    /// Safe to call multiple times.
    /// </summary>
    public void DetectCurrentlyAppliedWallpaper()
    {
        try
        {
            string? path = WallpaperService.GetCurrent();
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                CurrentImageFullPath = path;
                StatusHeadline = "Wallpaper detected";
            }
        }
        catch
        {
            // Non-fatal; leave placeholders in place.
        }
    }

    public ObservableCollection<SearchResultViewModel> SearchResults { get; }
    public ObservableCollection<FavoriteItem> Favorites { get; }
    public ObservableCollection<HistoryItem> History { get; }
    public ObservableCollection<CollectionItem> Collections { get; }
    public ObservableCollection<FavoriteItem> FilteredFavorites { get; }

    public PagedView<FavoriteItem> PagedFavorites { get; }
    public PagedView<HistoryItem> PagedHistory { get; }
    public ObservableCollection<string> RecentTags { get; }
    public DateTimeOffset SessionStartedAt { get; }

    public IReadOnlyList<string> LayoutModes { get; }
    public IReadOnlyList<string> OrientationOptions { get; }
    public IReadOnlyList<string> ColorToneOptions { get; }

    // Migrated to partial properties to silence MVVMTK0045 (the source generator can emit
    // CsWinRT-marshalling-friendly code when the property is partial). Initial values that
    // aren't default(T) are assigned in the constructor — partial properties forbid initializers.

    [ObservableProperty] public partial string StatusHeadline { get; set; }
    [ObservableProperty] public partial string CurrentImageFullPath { get; set; }
    public string CurrentImage =>
        string.IsNullOrWhiteSpace(CurrentImageFullPath) ? "No wallpaper applied yet" : Path.GetFileName(CurrentImageFullPath);

    partial void OnCurrentImageFullPathChanged(string value) => OnPropertyChanged(nameof(CurrentImage));
    [ObservableProperty] public partial bool IsEngineRunning { get; set; }
    [ObservableProperty] public partial string SearchTag { get; set; }
    [ObservableProperty] public partial bool IsSearching { get; set; }
    [ObservableProperty] public partial string SearchStatus { get; set; }
    [ObservableProperty] public partial string SelectedLayoutMode { get; set; }
    [ObservableProperty] public partial string SelectedOrientation { get; set; }
    [ObservableProperty] public partial string SelectedColorTone { get; set; }
    [ObservableProperty] public partial double MinWidth { get; set; }
    [ObservableProperty] public partial double MinHeight { get; set; }
    [ObservableProperty] public partial string SelectedMonitorId { get; set; }
    [ObservableProperty] public partial bool IsTrayEnabled { get; set; }
    [ObservableProperty] public partial bool IsStartupEnabled { get; set; }
    [ObservableProperty] public partial string NewCollectionName { get; set; }
    [ObservableProperty] public partial string SelectedCollectionName { get; set; }
    [ObservableProperty] public partial string SelectedSearchView { get; set; }
    [ObservableProperty] public partial string SelectedLibraryTab { get; set; }
    [ObservableProperty] public partial int SearchPage { get; set; }
    [ObservableProperty] public partial int SearchPerPage { get; set; }
    [ObservableProperty] public partial int SearchTotalResults { get; set; }
    [ObservableProperty] public partial int SearchTotalPages { get; set; }
    [ObservableProperty] public partial bool CanGoPrevSearchPage { get; set; }
    [ObservableProperty] public partial bool CanGoNextSearchPage { get; set; }
    [ObservableProperty] public partial string SearchPageLabel { get; set; }
    [ObservableProperty] public partial SystemSpecs SystemSpecs { get; set; }
    [ObservableProperty] public partial bool IsLoadingSystemSpecs { get; set; }
    [ObservableProperty] public partial bool SupportsRichEffects { get; set; }
    [ObservableProperty] public partial string SelectedTheme { get; set; }
    [ObservableProperty] public partial int WindowWidth { get; set; }
    [ObservableProperty] public partial int WindowHeight { get; set; }
    [ObservableProperty] public partial int WindowLeft { get; set; }
    [ObservableProperty] public partial int WindowTop { get; set; }
    [ObservableProperty] public partial string FavoritesFilter { get; set; }
    [ObservableProperty] public partial string FavoritesSort { get; set; }
    [ObservableProperty] public partial bool IsSidebarCollapsed { get; set; }
    [ObservableProperty] public partial string AutoChangeMode { get; set; }
    [ObservableProperty] public partial string UserImagesFolder { get; set; }
    public bool HasUserImagesFolder => !string.IsNullOrWhiteSpace(UserImagesFolder) && Directory.Exists(UserImagesFolder);
    partial void OnUserImagesFolderChanged(string value) => OnPropertyChanged(nameof(HasUserImagesFolder));
    [ObservableProperty] public partial TimeSpan MorningStart { get; set; }
    [ObservableProperty] public partial TimeSpan NoonStart { get; set; }
    [ObservableProperty] public partial TimeSpan EveningStart { get; set; }
    [ObservableProperty] public partial TimeSpan NightStart { get; set; }
    public IReadOnlyList<string> AutoChangeOptions { get; } = AutoChangeModes.All;
    [ObservableProperty] public partial bool IsBulkSelectEnabled { get; set; }
    [ObservableProperty] public partial int SelectedResultsCount { get; set; }
    [ObservableProperty] public partial string LastSearchError { get; set; }
    [ObservableProperty] public partial bool HasSearchError { get; set; }
    [ObservableProperty] public partial bool MatchScreenAspect { get; set; }
    [ObservableProperty] public partial double AspectTolerance { get; set; }

    private Task RunOnUiThreadAsync(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public void Dispose()
    {
        TryLogged(() => _engineCancellation?.Cancel(), nameof(Dispose) + ".cancel");
        // Flush one last snapshot before tearing down the debounce timer.
        TryLogged(FlushProfileToDisk, nameof(Dispose) + ".flush");
        TryLogged(() => _saveDebounceTimer.Dispose(), nameof(Dispose) + ".timer");
        TryLogged(() => _engineLoopTask?.Wait(TimeSpan.FromSeconds(3)), nameof(Dispose) + ".wait");
        TryLogged(() => _engineCancellation?.Dispose(), nameof(Dispose) + ".cts-dispose");
        _cycleGate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        TryLogged(() => _engineCancellation?.Cancel(), nameof(DisposeAsync) + ".cancel");
        TryLogged(FlushProfileToDisk, nameof(DisposeAsync) + ".flush");
        TryLogged(() => _saveDebounceTimer.Dispose(), nameof(DisposeAsync) + ".timer");
        if (_engineLoopTask is not null)
        {
            try { await _engineLoopTask; } catch (Exception ex) { EngineLog.Write($"DisposeAsync.await: {ex.Message}"); }
        }
        TryLogged(() => _engineCancellation?.Dispose(), nameof(DisposeAsync) + ".cts-dispose");
        _cycleGate.Dispose();
    }

    /// <summary>Invokes <paramref name="action"/>, swallowing exceptions into the engine log with a breadcrumb.</summary>
    private static void TryLogged(Action action, string tag)
    {
        try { action(); } catch (Exception ex) { EngineLog.Write($"{tag}: {ex.Message}"); }
    }
}

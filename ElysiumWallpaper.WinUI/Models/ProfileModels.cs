namespace ElysiumWallpaper.Models;

public sealed class AppProfile
{
    public string LayoutMode { get; set; } = "Fill";
    public string OrientationFilter { get; set; } = "Any";
    public string ColorToneFilter { get; set; } = "Any";
    public int MinWidth { get; set; } = 0;
    public int MinHeight { get; set; } = 0;
    public bool TrayEnabled { get; set; }
    public bool StartupEnabled { get; set; }
    public string SelectedMonitorId { get; set; } = Services.WallpaperService.AllMonitors;
    public int ActiveCollectionIndex { get; set; } = 0;
    public List<FavoriteItem> Favorites { get; set; } = [];
    public List<HistoryItem> History { get; set; } = [];
    public List<CollectionItem> Collections { get; set; } = [];
    public List<string> RecentTags { get; set; } = [];
    public string Theme { get; set; } = "Default";
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int WindowLeft { get; set; }
    public int WindowTop { get; set; }
    public string FavoritesFilter { get; set; } = "";
    public string FavoritesSort { get; set; } = "Newest";
    public string AutoChangeMode { get; set; } = ViewModels.AutoChangeModes.TimeOfDay;
    /// <summary>Optional local folder the user pointed at. Adds its images to the interval-mode pool.</summary>
    public string UserImagesFolder { get; set; } = "";
    public string MorningStart { get; set; } = "06:00";
    public string NoonStart { get; set; } = "12:00";
    public string EveningStart { get; set; } = "17:00";
    public string NightStart { get; set; } = "20:00";
}

public sealed class FavoriteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class HistoryItem
{
    public string ImagePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.Now;
    /// <summary>Who applied this wallpaper: "user" (manual pick) or "engine" (slot rotation).</summary>
    public string AppliedBy { get; set; } = "user";
}

public sealed class CollectionItem
{
    public string Name { get; set; } = string.Empty;
    public List<string> FavoriteIds { get; set; } = [];
    public int NextIndex { get; set; }
}

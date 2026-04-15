namespace ElysiumWallpaper.Models;

/// <summary>
/// Monitor-identity constants shared across Models / Services / ViewModels. Lives in Models
/// so <see cref="AppProfile.SelectedMonitorId"/> can default-initialize without taking a
/// compile-time dependency on <c>Services.WallpaperService</c> (that would invert the
/// intended layering: Services should depend on Models, not vice-versa).
/// </summary>
public static class MonitorConstants
{
    /// <summary>Sentinel monitor-id meaning "apply to every display on this machine".</summary>
    public const string AllMonitors = "ALL";
}

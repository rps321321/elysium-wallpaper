using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace ElysiumWallpaper.Services;

public sealed class MonitorOption
{
    public string Id { get; init; } = WallpaperService.AllMonitors;
    public string Name { get; init; } = "All Monitors";
}

public static class WallpaperService
{
    /// <summary>Sentinel monitor ID meaning "all connected displays".</summary>
    public const string AllMonitors = "ALL";

    private const uint SpiSetDeskWallpaper = 0x0014;
    private const uint SpiGetDeskWallpaper = 0x0073;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendWinIniChange = 0x0002;

    /// <summary>
    /// Returns the path to the currently-applied desktop wallpaper, or <c>null</c> if Windows
    /// reports a non-file slideshow / solid color / an invalid path. Uses the preferred
    /// <see cref="IDesktopWallpaper"/> COM API first (which handles per-monitor wallpapers)
    /// and falls back to <c>SPI_GETDESKWALLPAPER</c>.
    /// </summary>
    /// <summary>Per-monitor wallpaper path lookup via IDesktopWallpaper. Returns null if unavailable.</summary>
    public static string? GetWallpaperForMonitor(string monitorId)
    {
        if (string.IsNullOrWhiteSpace(monitorId)) return null;
        try
        {
            var desktop = (IDesktopWallpaper)new DesktopWallpaperClass();
            desktop.GetWallpaper(monitorId, out string path);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            return EnsureDecodableCopy(path);
        }
        catch
        {
            return null;
        }
    }

    public static string? GetCurrent()
    {
        try
        {
            var desktop = (IDesktopWallpaper)new DesktopWallpaperClass();
            desktop.GetMonitorDevicePathCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                desktop.GetMonitorDevicePathAt(i, out string monitorId);
                desktop.GetWallpaper(monitorId, out string path);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch
        {
            // Fall through to SPI / TranscodedWallpaper fallbacks.
        }

        var sb = new System.Text.StringBuilder(520);
        if (SystemParametersInfoGet(SpiGetDeskWallpaper, (uint)sb.Capacity, sb, 0))
        {
            string legacy = sb.ToString();
            if (!string.IsNullOrWhiteSpace(legacy) && File.Exists(legacy))
            {
                return legacy;
            }
        }

        // Last resort: Windows always writes the effective wallpaper to this transcoded cache,
        // even for Spotlight / slideshow / theme-driven sources that don't report a file path.
        string transcoded = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Themes\TranscodedWallpaper");
        return File.Exists(transcoded) ? EnsureDecodableCopy(transcoded) : null;
    }

    /// <summary>
    /// WIC decoders in WinUI's BitmapImage sometimes bail on files without an extension.
    /// Transparently shim such paths through a cached <c>.jpg</c>-extensioned copy in %TEMP%.
    /// </summary>
    private static string EnsureDecodableCopy(string sourcePath)
    {
        string ext = Path.GetExtension(sourcePath);
        if (!string.IsNullOrEmpty(ext)) return sourcePath;

        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ElysiumWallpaper");
            Directory.CreateDirectory(tempDir);
            string shim = Path.Combine(tempDir, "current-wallpaper.jpg");

            var src = new FileInfo(sourcePath);
            var dst = new FileInfo(shim);
            if (!dst.Exists || dst.LastWriteTimeUtc < src.LastWriteTimeUtc || dst.Length != src.Length)
            {
                File.Copy(sourcePath, shim, overwrite: true);
            }
            return shim;
        }
        catch
        {
            return sourcePath;
        }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoGet(uint uiAction, uint uiParam, System.Text.StringBuilder pvParam, uint fWinIni);

    public static IReadOnlyList<MonitorOption> GetMonitors()
    {
        var options = new List<MonitorOption> { new() { Id = AllMonitors, Name = "All Monitors" } };

        try
        {
            var desktop = (IDesktopWallpaper)new DesktopWallpaperClass();
            desktop.GetMonitorDevicePathCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                desktop.GetMonitorDevicePathAt(i, out string monitorId);
                options.Add(new MonitorOption { Id = monitorId, Name = $"Monitor {i + 1}" });
            }
        }
        catch
        {
        }

        return options;
    }

    public static bool Apply(string imagePath, string layoutMode, string monitorId)
    {
        if (!File.Exists(imagePath))
        {
            return false;
        }

        ApplyLayout(layoutMode);
        if (string.Equals(monitorId, AllMonitors, StringComparison.OrdinalIgnoreCase))
        {
            return SystemParametersInfo(
                SpiSetDeskWallpaper, 0, imagePath, SpifUpdateIniFile | SpifSendWinIniChange);
        }

        try
        {
            var desktop = (IDesktopWallpaper)new DesktopWallpaperClass();
            desktop.SetPosition(ParsePosition(layoutMode));
            desktop.SetWallpaper(monitorId, imagePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyLayout(string layoutMode)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
        if (key is null)
        {
            return;
        }

        // Registry-compatible style mapping for classic wallpaper behavior.
        switch (layoutMode)
        {
            case "Fill":
                key.SetValue("WallpaperStyle", "10");
                key.SetValue("TileWallpaper", "0");
                break;
            case "Fit":
                key.SetValue("WallpaperStyle", "6");
                key.SetValue("TileWallpaper", "0");
                break;
            case "Stretch":
                key.SetValue("WallpaperStyle", "2");
                key.SetValue("TileWallpaper", "0");
                break;
            case "Center":
                key.SetValue("WallpaperStyle", "0");
                key.SetValue("TileWallpaper", "0");
                break;
            case "Span":
                key.SetValue("WallpaperStyle", "22");
                key.SetValue("TileWallpaper", "0");
                break;
            default:
                key.SetValue("WallpaperStyle", "10");
                key.SetValue("TileWallpaper", "0");
                break;
        }
    }

    private static DesktopWallpaperPosition ParsePosition(string layoutMode) =>
        layoutMode switch
        {
            "Fill" => DesktopWallpaperPosition.Fill,
            "Fit" => DesktopWallpaperPosition.Fit,
            "Stretch" => DesktopWallpaperPosition.Stretch,
            "Center" => DesktopWallpaperPosition.Center,
            "Span" => DesktopWallpaperPosition.Span,
            _ => DesktopWallpaperPosition.Fill
        };

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    [ComImport, Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass;

    [ComImport, Guid("B92B56A9-8B55-4e14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        void GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorID);
        void GetMonitorDevicePathCount(out uint count);
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);
        void SetBackgroundColor(uint color);
        void GetBackgroundColor(out uint color);
        void SetPosition(DesktopWallpaperPosition position);
        void GetPosition(out DesktopWallpaperPosition position);
        void SetSlideshow(IntPtr items);
        void GetSlideshow(out IntPtr items);
        void SetSlideshowOptions(uint options, uint slideshowTick);
        void GetSlideshowOptions(out uint options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, uint direction);
        void GetStatus(out uint state);
        void Enable(bool enable);
    }

    private enum DesktopWallpaperPosition
    {
        Center = 0,
        Tile = 1,
        Stretch = 2,
        Fit = 3,
        Fill = 4,
        Span = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

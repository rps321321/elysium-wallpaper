namespace ElysiumWallpaper.Services;

public static class AppActions
{
    public static event Action? ApplyNowRequested;
    public static event Action? ToggleEngineRequested;
    public static event Action<bool>? TrayModeChanged;

    public static void RequestApplyNow() => ApplyNowRequested?.Invoke();
    public static void RequestToggleEngine() => ToggleEngineRequested?.Invoke();
    public static void SetTrayMode(bool enabled) => TrayModeChanged?.Invoke(enabled);
}

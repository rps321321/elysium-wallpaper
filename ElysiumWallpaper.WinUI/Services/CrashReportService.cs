using System.Diagnostics;

namespace ElysiumWallpaper.Services;

/// <summary>
/// Manages the post-crash recovery flow: reads the crash log written by
/// <c>App.OnAppUnhandledException</c>, exposes its contents to the UI, and provides
/// dismiss/open actions.
///
/// Lives in <c>%LocalAppData%\ElysiumWallpaper\crash-log.txt</c> alongside the engine log
/// and profile so all app state has a single home directory.
///
/// "Recent" means the file's last write was within the last 7 days — old logs are ignored
/// (the user already saw and dismissed them, or they're stale).
/// </summary>
public static class CrashReportService
{
    private static readonly TimeSpan RecencyWindow = TimeSpan.FromDays(7);

    private static string LogDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ElysiumWallpaper");

    public static string LogPath => Path.Combine(LogDirectory, "crash-log.txt");

    /// <summary>
    /// Append a crash entry. Called from the unhandled-exception handler in App.xaml.cs.
    /// Best-effort: never throws.
    /// </summary>
    public static void Append(string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            string body = exception?.ToString() ?? "(no exception object)";
            File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {message}\n{body}\n\n");
        }
        catch
        {
            // Last-ditch path: writing the log itself failed. Nothing useful to do here —
            // we're already in a crash flow, can't risk recursive failure.
        }
    }

    /// <summary>True iff a crash log exists and was written within <see cref="RecencyWindow"/>.</summary>
    public static bool HasRecentCrash()
    {
        try
        {
            var fi = new FileInfo(LogPath);
            return fi.Exists && fi.Length > 0 && (DateTime.Now - fi.LastWriteTime) < RecencyWindow;
        }
        catch { return false; }
    }

    /// <summary>Reads the log contents. Returns empty string on any IO failure.</summary>
    public static string ReadLog()
    {
        try { return File.Exists(LogPath) ? File.ReadAllText(LogPath) : string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Marks the current crash log as seen by renaming it with a timestamp suffix. The next
    /// <see cref="HasRecentCrash"/> call will return false until a new crash writes a fresh file.
    /// Old archives accumulate but are ~few KB each — leaving cleanup to the user.
    /// </summary>
    public static void Dismiss()
    {
        try
        {
            if (!File.Exists(LogPath)) return;
            string archive = Path.Combine(
                LogDirectory,
                $"crash-log.{DateTime.Now:yyyyMMdd-HHmmss}.txt.bak");
            File.Move(LogPath, archive, overwrite: true);
        }
        catch (Exception ex)
        {
            EngineLog.Write($"crash-dismiss failed: {ex.Message}");
        }
    }

    /// <summary>Opens the crash log directory in Explorer with the file pre-selected.</summary>
    public static void RevealInExplorer()
    {
        try
        {
            if (!File.Exists(LogPath)) return;
            Process.Start("explorer.exe", $"/select,\"{LogPath}\"");
        }
        catch (Exception ex)
        {
            EngineLog.Write($"crash-reveal failed: {ex.Message}");
        }
    }
}

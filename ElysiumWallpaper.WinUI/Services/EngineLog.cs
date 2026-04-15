namespace ElysiumWallpaper.Services;

/// <summary>
/// Append-only rolling log for engine events - debugging aid when auto-rotation misbehaves.
/// Writes to %LocalAppData%\ElysiumWallpaper\engine.log. Best-effort: never throws.
/// </summary>
public static class EngineLog
{
    private const long MaxBytes = 512 * 1024;  // 512 KB rolling window
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ElysiumWallpaper");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "engine.log");

            lock (Gate)
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    // Simple roll: trim to the last ~half and continue.
                    string[] lines = File.ReadAllLines(path);
                    File.WriteAllLines(path, lines.Skip(lines.Length / 2));
                }
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
        }
        catch
        {
            // Swallow - logging must never take down the engine.
        }
    }
}

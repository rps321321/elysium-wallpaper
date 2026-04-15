namespace ElysiumWallpaper.Services;

/// <summary>
/// Resolves the user's Pexels API key from local sources only — never from compiled-in
/// constants. The previous build embedded a key in source which leaked the moment the repo
/// went public; this replaces that pattern.
///
/// Resolution order (first non-empty wins):
/// <list type="number">
///   <item>Environment variable <c>ELYSIUM_PEXELS_API_KEY</c></item>
///   <item>File <c>%LocalAppData%\ElysiumWallpaper\pexels.key</c> (single-line, key only)</item>
/// </list>
///
/// When no key is found, returns <c>null</c>. Callers must handle null gracefully — typically
/// by skipping Pexels and falling through to the Openverse provider.
///
/// Cached for the process lifetime: changing the env var or key file requires an app restart.
/// </summary>
public static class PexelsKeyProvider
{
    private const string EnvVarName = "ELYSIUM_PEXELS_API_KEY";
    private const string KeyFileName = "pexels.key";

    private static readonly Lazy<string?> Cached = new(LoadKey, isThreadSafe: true);

    /// <summary>The resolved API key, or null when the user hasn't configured one.</summary>
    public static string? Key => Cached.Value;

    /// <summary>True when a Pexels key is available — callers can use this to gate features.</summary>
    public static bool HasKey => !string.IsNullOrWhiteSpace(Cached.Value);

    private static string? LoadKey()
    {
        try
        {
            string? fromEnv = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

            string keyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ElysiumWallpaper", KeyFileName);
            if (File.Exists(keyPath))
            {
                string fromFile = File.ReadAllText(keyPath).Trim();
                if (!string.IsNullOrWhiteSpace(fromFile)) return fromFile;
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"PexelsKeyProvider: load failed: {ex.Message}");
        }
        return null;
    }
}

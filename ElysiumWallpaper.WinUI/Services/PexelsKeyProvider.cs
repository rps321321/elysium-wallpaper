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
/// <see cref="Save(string)"/> and <see cref="Clear"/> write/delete the local key file and
/// invalidate the in-memory cache so the Settings UI can change the key without an app
/// restart. (The env-var path still requires a restart — OS limitation.)
/// </summary>
public static class PexelsKeyProvider
{
    private const string EnvVarName = "ELYSIUM_PEXELS_API_KEY";
    private const string KeyFileName = "pexels.key";

    private static readonly object CacheLock = new();
    private static bool _cacheLoaded;
    private static string? _cached;

    /// <summary>Absolute path to the key file (whether it exists or not).</summary>
    public static string KeyFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ElysiumWallpaper", KeyFileName);

    /// <summary>The resolved API key, or null when the user hasn't configured one.</summary>
    public static string? Key
    {
        get
        {
            lock (CacheLock)
            {
                if (!_cacheLoaded)
                {
                    _cached = LoadKey();
                    _cacheLoaded = true;
                }
                return _cached;
            }
        }
    }

    /// <summary>True when a Pexels key is available — callers can use this to gate features.</summary>
    public static bool HasKey => !string.IsNullOrWhiteSpace(Key);

    /// <summary>
    /// Writes the key to the local key file and refreshes the cache. Empty/whitespace input
    /// is treated as a no-op so the caller doesn't accidentally overwrite a good key with
    /// an empty textbox. Best-effort: throws on filesystem errors the caller should surface.
    /// </summary>
    public static void Save(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        string trimmed = key.Trim();

        string dir = Path.GetDirectoryName(KeyFilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(KeyFilePath, trimmed);

        lock (CacheLock)
        {
            _cached = trimmed;
            _cacheLoaded = true;
        }
        EngineLog.Write("PexelsKeyProvider: key saved");
    }

    /// <summary>
    /// Removes the local key file (the env-var path can't be cleared by a running app) and
    /// refreshes the cache. After this, <see cref="HasKey"/> reflects only the env var.
    /// </summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(KeyFilePath)) File.Delete(KeyFilePath);
        }
        catch (Exception ex)
        {
            EngineLog.Write($"PexelsKeyProvider: clear failed: {ex.Message}");
            throw;
        }

        lock (CacheLock)
        {
            // Re-resolve so the env-var fallback (if any) is picked up immediately.
            _cached = LoadKey();
            _cacheLoaded = true;
        }
        EngineLog.Write("PexelsKeyProvider: key cleared");
    }

    /// <summary>Forces a re-read from env+disk. Useful after external changes.</summary>
    public static void Invalidate()
    {
        lock (CacheLock) { _cacheLoaded = false; }
    }

    private static string? LoadKey()
    {
        try
        {
            string? fromEnv = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();

            if (File.Exists(KeyFilePath))
            {
                string fromFile = File.ReadAllText(KeyFilePath).Trim();
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

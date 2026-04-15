namespace ElysiumWallpaper.Services;

/// <summary>
/// Parses the legacy <c>slot_manifest.txt</c> file written by <c>fetch_pexels.py</c>.
/// Format: one <c>key=value</c> pair per line, blank lines and lines without <c>=</c> are ignored.
/// </summary>
public static class SlotManifest
{
    public const string FileName = "slot_manifest.txt";

    /// <summary>Parses manifest lines into a case-insensitive lookup. Pure - takes an enumerable so tests can pass literals.</summary>
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            int splitAt = raw.IndexOf('=');
            if (splitAt <= 0)
            {
                continue;
            }

            string key = raw[..splitAt].Trim();
            string value = raw[(splitAt + 1)..].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            result[key] = value;
        }
        return result;
    }

    /// <summary>Loads manifest from disk. Returns an empty dictionary if the file does not exist.</summary>
    public static IReadOnlyDictionary<string, string> Load(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return Parse(File.ReadAllLines(manifestPath));
    }

    // Per-path mtime-keyed cache so hot paths don't re-parse the file every cycle.
    private static readonly Dictionary<string, (DateTime mtime, IReadOnlyDictionary<string, string> data)> Cache = new();
    private static readonly object CacheLock = new();

    /// <summary>
    /// Loads + caches. Next call returns the cached dict if the file's mtime hasn't moved.
    /// Thread-safe (cheap dictionary lookup under a lock).
    /// </summary>
    public static IReadOnlyDictionary<string, string> LoadCached(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            lock (CacheLock) Cache.Remove(manifestPath);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // Hold the lock across mtime read + file read + cache write so a concurrent writer
        // can't sneak between the steps and cause us to cache stale parsed data against
        // the new mtime — which would then look fresh forever until the next write.
        // Manifest files are tiny (a handful of lines), so the lock duration is negligible.
        lock (CacheLock)
        {
            DateTime mtime = File.GetLastWriteTimeUtc(manifestPath);
            if (Cache.TryGetValue(manifestPath, out var entry) && entry.mtime == mtime)
            {
                return entry.data;
            }
            var parsed = Parse(File.ReadAllLines(manifestPath));
            Cache[manifestPath] = (mtime, parsed);
            return parsed;
        }
    }

    /// <summary>Explicitly invalidate the cached entry for a manifest path (e.g. after a fetch rewrites it).</summary>
    public static void InvalidateCache(string manifestPath)
    {
        lock (CacheLock) Cache.Remove(manifestPath);
    }

    /// <summary>Wipe every cached entry. Intended for unit tests; no production caller.</summary>
    public static void ClearCacheForTests()
    {
        lock (CacheLock) Cache.Clear();
    }
}

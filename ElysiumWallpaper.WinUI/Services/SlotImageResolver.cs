namespace ElysiumWallpaper.Services;

/// <summary>
/// Resolves the wallpaper file that belongs to a given time slot.
/// Priority: manifest override > conventional <c>{slot}.{ext}</c> filename.
/// </summary>
public static class SlotImageResolver
{
    public static readonly string[] KnownExtensions = [".jpg", ".jpeg", ".png", ".bmp"];

    /// <summary>
    /// Returns the absolute path to the slot image, or <c>null</c> when no candidate exists.
    /// Takes a <paramref name="fileExists"/> delegate so the resolver can be tested without touching disk.
    /// </summary>
    public static string? Resolve(
        string slotKey,
        string imagesDir,
        IReadOnlyDictionary<string, string> manifest,
        Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagesDir);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(fileExists);

        if (manifest.TryGetValue(slotKey, out var manifestFile)
            && !string.IsNullOrWhiteSpace(manifestFile))
        {
            string fromManifest = Path.Combine(imagesDir, manifestFile);
            if (fileExists(fromManifest))
            {
                return fromManifest;
            }
        }

        foreach (var ext in KnownExtensions)
        {
            string candidate = Path.Combine(imagesDir, slotKey + ext);
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Disk-backed overload using <see cref="File.Exists(string)"/>.</summary>
    public static string? Resolve(string slotKey, string imagesDir, IReadOnlyDictionary<string, string> manifest)
        => Resolve(slotKey, imagesDir, manifest, File.Exists);
}

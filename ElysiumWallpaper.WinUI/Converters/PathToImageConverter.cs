using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ElysiumWallpaper.Converters;

/// <summary>
/// Binds an absolute local file path to a <see cref="BitmapImage"/> for use as an <c>Image.Source</c>.
/// Returns <c>null</c> for missing paths so the Image control stays blank instead of crashing.
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    // Cache by (path, mtime) — if the file is rewritten in place, mtime changes and we refetch.
    // BitmapImage holds a WIC-decoded texture (potentially tens of MB at 4K). The cache values
    // are WeakReference so the GC can reclaim them under memory pressure; the (path, mtime) key
    // dedups within a single XAML pass. CacheCapacity caps strong-reference count via the
    // UsageOrder LRU; weak entries beyond it stay around only until GC.
    private const int CacheCapacity = 16;
    private static readonly LinkedList<string> UsageOrder = new();
    private static readonly Dictionary<string, (DateTime mtime, WeakReference<BitmapImage> imageRef)> Cache
        = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) return null;

        DateTime mtime;
        try { mtime = File.GetLastWriteTimeUtc(path); }
        catch { return SafeCreate(path); }

        lock (CacheLock)
        {
            if (Cache.TryGetValue(path, out var entry)
                && entry.mtime == mtime
                && entry.imageRef.TryGetTarget(out var existing))
            {
                Touch(path);
                return existing;
            }
        }

        var bmp = SafeCreate(path);
        if (bmp is null) return null;

        lock (CacheLock)
        {
            Cache[path] = (mtime, new WeakReference<BitmapImage>(bmp));
            Touch(path);
            while (UsageOrder.Count > CacheCapacity && UsageOrder.First is { } oldest)
            {
                Cache.Remove(oldest.Value);
                UsageOrder.RemoveFirst();
            }
        }
        return bmp;
    }

    private static BitmapImage? SafeCreate(string path)
    {
        try { return new BitmapImage(new Uri(path, UriKind.Absolute)); }
        catch { return null; }
    }

    private static void Touch(string path)
    {
        // Caller already holds CacheLock.
        var node = UsageOrder.Find(path);
        if (node is not null) UsageOrder.Remove(node);
        UsageOrder.AddLast(path);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

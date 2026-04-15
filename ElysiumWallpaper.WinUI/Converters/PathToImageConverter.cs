using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ElysiumWallpaper.Converters;

/// <summary>
/// Binds an absolute local file path to a <see cref="BitmapImage"/> for use as an <c>Image.Source</c>.
/// Returns <c>null</c> for missing paths so the Image control stays blank instead of crashing.
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    // Cache by (path, mtime) - if the file is rewritten in place, mtime changes and we refetch.
    // A small bounded LRU keeps memory in check when the library grows.
    private const int CacheCapacity = 64;
    private static readonly LinkedList<string> UsageOrder = new();
    private static readonly Dictionary<string, (DateTime mtime, BitmapImage image)> Cache = new(StringComparer.OrdinalIgnoreCase);
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
            if (Cache.TryGetValue(path, out var entry) && entry.mtime == mtime)
            {
                Touch(path);
                return entry.image;
            }
        }

        var bmp = SafeCreate(path);
        if (bmp is null) return null;

        lock (CacheLock)
        {
            Cache[path] = (mtime, bmp);
            Touch(path);
            while (Cache.Count > CacheCapacity && UsageOrder.First is { } oldest)
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

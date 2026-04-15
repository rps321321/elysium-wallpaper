namespace ElysiumWallpaper.ViewModels;

public sealed class SearchResultViewModel
{
    public SearchResultViewModel(string tag, string previewUrl, string downloadUrl, int width, int height, string photographer)
    {
        Tag = tag;
        PreviewUrl = previewUrl;
        DownloadUrl = downloadUrl;
        Width = width;
        Height = height;
        Photographer = photographer;
    }

    public string Tag { get; }
    public string PreviewUrl { get; }
    public string DownloadUrl { get; }
    public int Width { get; }
    public int Height { get; }
    public string Photographer { get; }
    public string Resolution => $"{Width}x{Height}";
    public string QualityBadge => Width switch
    {
        >= 7680 => "8K",
        >= 3840 => "4K",
        >= 2560 => "QHD",
        >= 1920 => "FHD",
        >= 1280 => "HD",
        _ => "SD"
    };
}

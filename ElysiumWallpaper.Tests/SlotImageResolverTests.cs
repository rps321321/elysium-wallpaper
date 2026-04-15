namespace ElysiumWallpaper.Tests;

public class SlotImageResolverTests
{
    private const string ImagesDir = @"C:\images";

    [Fact]
    public void Prefers_manifest_entry_when_file_exists()
    {
        var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["morning"] = "custom-dawn.png"
        };
        bool fileExists(string p) => p == Path.Combine(ImagesDir, "custom-dawn.png");
        var result = SlotImageResolver.Resolve("morning", ImagesDir, manifest, fileExists);
        Assert.Equal(Path.Combine(ImagesDir, "custom-dawn.png"), result);
    }

    [Fact]
    public void Falls_back_to_conventional_filename_when_manifest_points_to_missing()
    {
        var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["morning"] = "missing.jpg"
        };
        bool fileExists(string p) => p == Path.Combine(ImagesDir, "morning.jpg");
        var result = SlotImageResolver.Resolve("morning", ImagesDir, manifest, fileExists);
        Assert.Equal(Path.Combine(ImagesDir, "morning.jpg"), result);
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".bmp")]
    public void Walks_known_extensions_in_order(string ext)
    {
        string onlyPath = Path.Combine(ImagesDir, "night" + ext);
        bool fileExists(string p) => p == onlyPath;
        var result = SlotImageResolver.Resolve("night", ImagesDir, new Dictionary<string, string>(), fileExists);
        Assert.Equal(onlyPath, result);
    }

    [Fact]
    public void First_extension_wins_when_multiple_candidates_exist()
    {
        bool fileExists(string _) => true;
        var result = SlotImageResolver.Resolve("noon", ImagesDir, new Dictionary<string, string>(), fileExists);
        Assert.Equal(Path.Combine(ImagesDir, "noon.jpg"), result);
    }

    [Fact]
    public void Returns_null_when_nothing_matches()
    {
        bool fileExists(string _) => false;
        var result = SlotImageResolver.Resolve("evening", ImagesDir, new Dictionary<string, string>(), fileExists);
        Assert.Null(result);
    }

    [Fact]
    public void Empty_manifest_value_skips_override()
    {
        var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["morning"] = "   "
        };
        bool fileExists(string p) => p == Path.Combine(ImagesDir, "morning.jpg");
        var result = SlotImageResolver.Resolve("morning", ImagesDir, manifest, fileExists);
        Assert.Equal(Path.Combine(ImagesDir, "morning.jpg"), result);
    }

    [Fact]
    public void Throws_on_null_or_blank_inputs()
    {
        Assert.Throws<ArgumentException>(() =>
            SlotImageResolver.Resolve("", ImagesDir, new Dictionary<string, string>(), _ => true));
        Assert.Throws<ArgumentException>(() =>
            SlotImageResolver.Resolve("morning", "", new Dictionary<string, string>(), _ => true));
        Assert.Throws<ArgumentNullException>(() =>
            SlotImageResolver.Resolve("morning", ImagesDir, null!, _ => true));
        Assert.Throws<ArgumentNullException>(() =>
            SlotImageResolver.Resolve("morning", ImagesDir, new Dictionary<string, string>(), null!));
    }
}

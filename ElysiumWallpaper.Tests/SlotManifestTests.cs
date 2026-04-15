namespace ElysiumWallpaper.Tests;

public class SlotManifestTests : IDisposable
{
    private readonly string _tempPath;

    public SlotManifestTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"ely-manifest-{Guid.NewGuid():N}.txt");
        SlotManifest.ClearCacheForTests();
    }

    public void Dispose()
    {
        try { File.Delete(_tempPath); } catch { }
        SlotManifest.ClearCacheForTests();
    }

    [Fact]
    public void Parse_returns_empty_for_empty_input()
    {
        Assert.Empty(SlotManifest.Parse([]));
    }

    [Fact]
    public void Parse_extracts_key_value_pairs()
    {
        var result = SlotManifest.Parse(
        [
            "morning=morning.jpg",
            "noon=noon.jpg",
            "evening=evening.jpg",
            "night=night.jpg"
        ]);

        Assert.Equal(4, result.Count);
        Assert.Equal("morning.jpg", result["morning"]);
        Assert.Equal("night.jpg", result["night"]);
    }

    [Fact]
    public void Parse_is_case_insensitive_on_keys()
    {
        var result = SlotManifest.Parse(["Morning=dawn.png"]);
        Assert.True(result.ContainsKey("morning"));
        Assert.True(result.ContainsKey("MORNING"));
    }

    [Fact]
    public void Parse_skips_blank_and_malformed_lines()
    {
        var result = SlotManifest.Parse(
        [
            "",
            "   ",
            "noequalshere",
            "=leadingequals",
            "morning=ok.jpg"
        ]);

        Assert.Single(result);
        Assert.Equal("ok.jpg", result["morning"]);
    }

    [Fact]
    public void Parse_trims_whitespace_around_key_and_value()
    {
        var result = SlotManifest.Parse(["  morning  =  morning.jpg  "]);
        Assert.Equal("morning.jpg", result["morning"]);
    }

    [Fact]
    public void Load_returns_empty_when_file_missing()
    {
        var result = SlotManifest.Load(Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.txt"));
        Assert.Empty(result);
    }

    [Fact]
    public void Load_round_trips_through_disk()
    {
        File.WriteAllText(_tempPath, "morning=m.jpg\nevening=e.png\n");
        var result = SlotManifest.Load(_tempPath);
        Assert.Equal("m.jpg", result["morning"]);
        Assert.Equal("e.png", result["evening"]);
    }

    [Fact]
    public void LoadCached_returns_same_instance_when_mtime_unchanged()
    {
        File.WriteAllText(_tempPath, "morning=m.jpg\n");
        var a = SlotManifest.LoadCached(_tempPath);
        var b = SlotManifest.LoadCached(_tempPath);
        Assert.Same(a, b);
    }

    [Fact]
    public void LoadCached_refreshes_when_file_mtime_changes()
    {
        // Write + read the first state, then capture its actual mtime so the "advanced"
        // mtime is deterministically > firstMtime. Earlier the test used DateTime.UtcNow
        // offsets which could collide on fast machines where UtcNow returned the same
        // tick twice.
        File.WriteAllText(_tempPath, "morning=old.jpg\n");
        var a = SlotManifest.LoadCached(_tempPath);
        Assert.Equal("old.jpg", a["morning"]);
        DateTime firstMtime = File.GetLastWriteTimeUtc(_tempPath);

        // Write the new content FIRST so the file is flushed, then force mtime forward.
        // (Doing WriteAllText after SetLastWriteTimeUtc would clobber the advanced mtime
        // since WriteAllText updates it to "now".)
        File.WriteAllText(_tempPath, "morning=new.jpg\n");
        File.SetLastWriteTimeUtc(_tempPath, firstMtime.AddSeconds(5));

        var b = SlotManifest.LoadCached(_tempPath);
        Assert.NotSame(a, b);
        Assert.Equal("new.jpg", b["morning"]);
    }

    [Fact]
    public void InvalidateCache_forces_refresh_even_without_mtime_change()
    {
        File.WriteAllText(_tempPath, "morning=first.jpg\n");
        var a = SlotManifest.LoadCached(_tempPath);

        SlotManifest.InvalidateCache(_tempPath);

        var b = SlotManifest.LoadCached(_tempPath);
        Assert.NotSame(a, b);  // new instance even though file didn't change
    }
}

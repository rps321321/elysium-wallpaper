namespace ElysiumWallpaper.Tests;

public class SlotBoundariesTests
{
    [Fact]
    public void Default_is_valid()
    {
        Assert.True(SlotBoundaries.Default.IsValid);
    }

    [Theory]
    [InlineData(6, 12, 17, 20, true)]   // standard
    [InlineData(5, 11, 18, 22, true)]   // shifted earlier/later
    [InlineData(6, 6, 17, 20, false)]    // morning == noon
    [InlineData(12, 6, 17, 20, false)]   // morning > noon
    [InlineData(6, 12, 12, 20, false)]   // noon == evening
    [InlineData(6, 12, 17, 17, false)]   // evening == night
    [InlineData(6, 12, 20, 17, false)]   // evening > night
    public void IsValid_enforces_strict_ordering(int m, int n, int e, int ni, bool expected)
    {
        var b = new SlotBoundaries(
            new TimeSpan(m, 0, 0),
            new TimeSpan(n, 0, 0),
            new TimeSpan(e, 0, 0),
            new TimeSpan(ni, 0, 0));
        Assert.Equal(expected, b.IsValid);
    }

    [Fact]
    public void IsValid_rejects_negative_or_over_24h_times()
    {
        var neg = new SlotBoundaries(new(-1, 0, 0), new(12, 0, 0), new(17, 0, 0), new(20, 0, 0));
        Assert.False(neg.IsValid);

        var over = new SlotBoundaries(new(6, 0, 0), new(12, 0, 0), new(17, 0, 0), TimeSpan.FromHours(25));
        Assert.False(over.IsValid);
    }

    [Fact]
    public void OrDefault_returns_self_when_valid()
    {
        var valid = new SlotBoundaries(new(7, 0, 0), new(12, 0, 0), new(17, 0, 0), new(20, 0, 0));
        Assert.Same(valid, valid.OrDefault());
    }

    [Fact]
    public void OrDefault_returns_Default_when_invalid()
    {
        var broken = new SlotBoundaries(new(20, 0, 0), new(17, 0, 0), new(12, 0, 0), new(6, 0, 0));
        Assert.Same(SlotBoundaries.Default, broken.OrDefault());
    }
}

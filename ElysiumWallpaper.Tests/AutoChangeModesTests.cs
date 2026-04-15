namespace ElysiumWallpaper.Tests;

public class AutoChangeModesTests
{
    [Fact]
    public void TimeOfDay_has_no_interval()
    {
        Assert.Null(AutoChangeModes.GetIntervalMinutes(AutoChangeModes.TimeOfDay));
    }

    [Theory]
    [InlineData("Every 5 minutes", 5)]
    [InlineData("Every 15 minutes", 15)]
    [InlineData("Every 30 minutes", 30)]
    [InlineData("Every 1 hour", 60)]
    [InlineData("Every 2 hours", 120)]
    [InlineData("Every 4 hours", 240)]
    [InlineData("Every 8 hours", 480)]
    public void Known_interval_strings_map_to_correct_minutes(string mode, int expected)
    {
        Assert.Equal(expected, AutoChangeModes.GetIntervalMinutes(mode));
    }

    [Theory]
    [InlineData("Every 10 minutes")]  // not in our supported list
    [InlineData("Hourly")]
    [InlineData("")]
    [InlineData("random garbage")]
    public void Unknown_modes_return_null(string mode)
    {
        Assert.Null(AutoChangeModes.GetIntervalMinutes(mode));
    }

    [Fact]
    public void All_constants_appear_in_All_list()
    {
        Assert.Contains(AutoChangeModes.TimeOfDay, AutoChangeModes.All);
        Assert.Contains(AutoChangeModes.Every5Min, AutoChangeModes.All);
        Assert.Contains(AutoChangeModes.Every8H, AutoChangeModes.All);
        Assert.Equal(8, AutoChangeModes.All.Count);  // 1 time-of-day + 7 intervals
    }

    [Fact]
    public void Every_entry_in_All_parses_cleanly()
    {
        foreach (var mode in AutoChangeModes.All)
        {
            // TimeOfDay returns null; everything else must be a valid positive minute count.
            var parsed = AutoChangeModes.GetIntervalMinutes(mode);
            if (mode == AutoChangeModes.TimeOfDay)
            {
                Assert.Null(parsed);
            }
            else
            {
                Assert.NotNull(parsed);
                Assert.True(parsed > 0);
            }
        }
    }
}

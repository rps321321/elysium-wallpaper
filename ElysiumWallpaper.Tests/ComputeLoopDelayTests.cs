namespace ElysiumWallpaper.Tests;

public class ComputeLoopDelayTests
{
    private static readonly SlotBoundaries Defaults = SlotBoundaries.Default;

    [Fact]
    public void Interval_mode_returns_the_parsed_minutes()
    {
        var d = MainViewModel.ComputeLoopDelay(DateTime.Now, AutoChangeModes.Every30Min, Defaults);
        Assert.Equal(TimeSpan.FromMinutes(30), d);
    }

    [Fact]
    public void Every_8_hours_returns_480_minutes()
    {
        var d = MainViewModel.ComputeLoopDelay(DateTime.Now, AutoChangeModes.Every8H, Defaults);
        Assert.Equal(TimeSpan.FromMinutes(480), d);
    }

    [Fact]
    public void TimeOfDay_wakes_at_next_slot_boundary_when_within_cap()
    {
        // 11:50 - next boundary is 12:00 - 10 minutes away, under 15-min cap.
        var now = new DateTime(2026, 4, 15, 11, 50, 0);
        var d = MainViewModel.ComputeLoopDelay(now, AutoChangeModes.TimeOfDay, Defaults);
        Assert.InRange(d, TimeSpan.FromMinutes(9.9), TimeSpan.FromMinutes(10.2));
    }

    [Fact]
    public void TimeOfDay_caps_at_15_minutes_when_next_boundary_is_further()
    {
        // 08:00 - next boundary is 12:00, 4h away. Should cap at 15 min.
        var now = new DateTime(2026, 4, 15, 8, 0, 0);
        var d = MainViewModel.ComputeLoopDelay(now, AutoChangeModes.TimeOfDay, Defaults);
        Assert.Equal(TimeSpan.FromMinutes(15), d);
    }

    [Fact]
    public void TimeOfDay_handles_midnight_wrap()
    {
        // 23:00 - next boundary is tomorrow 06:00. Should cap at 15 min (far future).
        var now = new DateTime(2026, 4, 15, 23, 0, 0);
        var d = MainViewModel.ComputeLoopDelay(now, AutoChangeModes.TimeOfDay, Defaults);
        Assert.Equal(TimeSpan.FromMinutes(15), d);
    }

    [Fact]
    public void TimeOfDay_honors_custom_boundaries()
    {
        var custom = new SlotBoundaries(new(7, 0, 0), new(13, 0, 0), new(18, 0, 0), new(22, 0, 0));
        // 12:55 - next custom boundary at 13:00, 5 min away.
        var now = new DateTime(2026, 4, 15, 12, 55, 0);
        var d = MainViewModel.ComputeLoopDelay(now, AutoChangeModes.TimeOfDay, custom);
        Assert.InRange(d, TimeSpan.FromMinutes(4.9), TimeSpan.FromMinutes(5.2));
    }
}

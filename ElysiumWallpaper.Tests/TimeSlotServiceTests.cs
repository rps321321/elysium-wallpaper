namespace ElysiumWallpaper.Tests;

public class TimeSlotServiceTests
{
    // Default boundaries: morning 06:00, noon 12:00, evening 17:00, night 20:00.

    [Theory]
    [InlineData(6, 0, TimeSlot.Morning)]
    [InlineData(7, 30, TimeSlot.Morning)]
    [InlineData(11, 59, TimeSlot.Morning)]
    [InlineData(12, 0, TimeSlot.Noon)]
    [InlineData(13, 0, TimeSlot.Noon)]
    [InlineData(16, 59, TimeSlot.Noon)]
    [InlineData(17, 0, TimeSlot.Evening)]
    [InlineData(18, 30, TimeSlot.Evening)]
    [InlineData(19, 59, TimeSlot.Evening)]
    [InlineData(20, 0, TimeSlot.Night)]
    [InlineData(23, 59, TimeSlot.Night)]
    [InlineData(0, 0, TimeSlot.Night)]
    [InlineData(3, 0, TimeSlot.Night)]
    [InlineData(5, 59, TimeSlot.Night)]
    public void Default_boundaries_map_hour_minute_to_expected_slot(int h, int m, TimeSlot expected)
    {
        var local = new DateTime(2026, 4, 15, h, m, 0);
        Assert.Equal(expected, TimeSlotService.GetSlotForTime(local));
    }

    [Fact]
    public void Custom_boundaries_override_defaults()
    {
        // Morning 07:30, noon 13:00, evening 18:00, night 22:00.
        var custom = new SlotBoundaries(
            new TimeSpan(7, 30, 0),
            new TimeSpan(13, 0, 0),
            new TimeSpan(18, 0, 0),
            new TimeSpan(22, 0, 0));

        Assert.Equal(TimeSlot.Night, TimeSlotService.GetSlotForTime(new DateTime(2026, 4, 15, 7, 0, 0), custom));
        Assert.Equal(TimeSlot.Morning, TimeSlotService.GetSlotForTime(new DateTime(2026, 4, 15, 7, 30, 0), custom));
        Assert.Equal(TimeSlot.Morning, TimeSlotService.GetSlotForTime(new DateTime(2026, 4, 15, 12, 59, 0), custom));
        Assert.Equal(TimeSlot.Noon, TimeSlotService.GetSlotForTime(new DateTime(2026, 4, 15, 13, 0, 0), custom));
        Assert.Equal(TimeSlot.Evening, TimeSlotService.GetSlotForTime(new DateTime(2026, 4, 15, 18, 0, 0), custom));
        Assert.Equal(TimeSlot.Night, TimeSlotService.GetSlotForTime(new DateTime(2026, 4, 15, 22, 0, 0), custom));
        Assert.Equal(TimeSlot.Night, TimeSlotService.GetSlotForTime(new DateTime(2026, 4, 15, 2, 0, 0), custom));
    }

    [Fact]
    public void Invalid_boundaries_fall_back_to_default()
    {
        // Out-of-order: morning after noon - invalid.
        var broken = new SlotBoundaries(
            new TimeSpan(12, 0, 0),
            new TimeSpan(6, 0, 0),
            new TimeSpan(17, 0, 0),
            new TimeSpan(20, 0, 0));
        Assert.False(broken.IsValid);

        // GetSlotForTime routes through OrDefault() so we expect default behavior.
        Assert.Equal(TimeSlot.Morning, TimeSlotService.GetSlotForTime(new DateTime(2026, 4, 15, 7, 0, 0), broken));
    }

    [Theory]
    [InlineData(TimeSlot.Morning, "morning")]
    [InlineData(TimeSlot.Noon, "noon")]
    [InlineData(TimeSlot.Evening, "evening")]
    [InlineData(TimeSlot.Night, "night")]
    public void Slot_keys_are_lowercase_ascii(TimeSlot slot, string expected)
    {
        Assert.Equal(expected, TimeSlotService.GetSlotKey(slot));
    }

    [Fact]
    public void NextBoundary_returns_next_transition_today()
    {
        var defaults = SlotBoundaries.Default;
        var now = new DateTime(2026, 4, 15, 8, 0, 0); // 8am morning
        var next = TimeSlotService.NextBoundary(now, defaults);
        Assert.Equal(new DateTime(2026, 4, 15, 12, 0, 0), next);
    }

    [Fact]
    public void NextBoundary_wraps_to_tomorrow_after_last_boundary()
    {
        var defaults = SlotBoundaries.Default;
        var now = new DateTime(2026, 4, 15, 23, 0, 0); // 11pm night
        var next = TimeSlotService.NextBoundary(now, defaults);
        Assert.Equal(new DateTime(2026, 4, 16, 6, 0, 0), next);
    }

    [Fact]
    public void NextBoundary_honors_custom_boundaries()
    {
        var custom = new SlotBoundaries(new(7, 0, 0), new(12, 0, 0), new(17, 0, 0), new(21, 0, 0));
        var now = new DateTime(2026, 4, 15, 10, 30, 0);
        var next = TimeSlotService.NextBoundary(now, custom);
        Assert.Equal(new DateTime(2026, 4, 15, 12, 0, 0), next);
    }
}

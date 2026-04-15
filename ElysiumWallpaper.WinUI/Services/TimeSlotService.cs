namespace ElysiumWallpaper.Services;

/// <summary>
/// Four wallpaper rotation windows over a 24-hour clock.
/// Boundaries match the original C++ daemon so existing manifests keep working.
/// </summary>
public enum TimeSlot
{
    /// <summary>06:00 - 11:59.</summary>
    Morning,
    /// <summary>12:00 - 16:59.</summary>
    Noon,
    /// <summary>17:00 - 19:59.</summary>
    Evening,
    /// <summary>20:00 - 05:59.</summary>
    Night
}

/// <summary>
/// User-editable slot boundary config. All four must be strictly monotonically increasing;
/// <see cref="Default"/> matches the legacy 6/12/17/20 hardcoded layout.
/// </summary>
public sealed record SlotBoundaries(
    TimeSpan MorningStart,
    TimeSpan NoonStart,
    TimeSpan EveningStart,
    TimeSpan NightStart)
{
    public static SlotBoundaries Default { get; } = new(
        new(6, 0, 0),
        new(12, 0, 0),
        new(17, 0, 0),
        new(20, 0, 0));

    /// <summary>Returns true iff MorningStart &lt; NoonStart &lt; EveningStart &lt; NightStart, all within 00:00-23:59.</summary>
    public bool IsValid
    {
        get
        {
            static bool InDay(TimeSpan t) => t >= TimeSpan.Zero && t < TimeSpan.FromDays(1);
            return InDay(MorningStart) && InDay(NoonStart) && InDay(EveningStart) && InDay(NightStart)
                && MorningStart < NoonStart && NoonStart < EveningStart && EveningStart < NightStart;
        }
    }

    /// <summary>Falls back to <see cref="Default"/> if any field is invalid or ordering breaks.</summary>
    public SlotBoundaries OrDefault() => IsValid ? this : Default;
}

/// <summary>
/// Pure time-slot math. No I/O, no clock access - all inputs supplied by caller so tests stay deterministic.
/// </summary>
public static class TimeSlotService
{
    /// <summary>Default-boundary convenience overload.</summary>
    public static TimeSlot GetSlotForTime(DateTime localTime) => GetSlotForTime(localTime, SlotBoundaries.Default);

    /// <summary>
    /// Maps a local DateTime to its slot using caller-supplied boundaries. Night wraps across midnight:
    /// anything &gt;= NightStart OR &lt; MorningStart is night.
    /// </summary>
    public static TimeSlot GetSlotForTime(DateTime localTime, SlotBoundaries boundaries)
    {
        boundaries = boundaries.OrDefault();
        TimeSpan t = localTime.TimeOfDay;
        if (t >= boundaries.MorningStart && t < boundaries.NoonStart) return TimeSlot.Morning;
        if (t >= boundaries.NoonStart && t < boundaries.EveningStart) return TimeSlot.Noon;
        if (t >= boundaries.EveningStart && t < boundaries.NightStart) return TimeSlot.Evening;
        return TimeSlot.Night;
    }

    /// <summary>Manifest key matching the slot (lower-case, ASCII).</summary>
    public static string GetSlotKey(TimeSlot slot) => slot switch
    {
        TimeSlot.Morning => "morning",
        TimeSlot.Noon => "noon",
        TimeSlot.Evening => "evening",
        TimeSlot.Night => "night",
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown TimeSlot value.")
    };

    /// <summary>Next local-time instant after <paramref name="now"/> at which the slot will change.</summary>
    public static DateTime NextBoundary(DateTime now, SlotBoundaries boundaries)
    {
        boundaries = boundaries.OrDefault();
        DateTime today = now.Date;
        foreach (var b in new[]
        {
            today.Add(boundaries.MorningStart),
            today.Add(boundaries.NoonStart),
            today.Add(boundaries.EveningStart),
            today.Add(boundaries.NightStart)
        })
        {
            if (b > now) return b;
        }
        return today.AddDays(1).Add(boundaries.MorningStart);
    }
}

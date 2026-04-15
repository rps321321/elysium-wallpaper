namespace ElysiumWallpaper.ViewModels;

/// <summary>
/// Canonical mode identifiers. Keeping them as <c>string</c> (rather than an enum) lets
/// ComboBox bind directly without a value converter while still giving a single source of
/// truth so typos at call sites become compile errors.
/// </summary>
public static class AutoChangeModes
{
    public const string TimeOfDay = "By time of day";
    public const string Every5Min = "Every 5 minutes";
    public const string Every15Min = "Every 15 minutes";
    public const string Every30Min = "Every 30 minutes";
    public const string Every1H = "Every 1 hour";
    public const string Every2H = "Every 2 hours";
    public const string Every4H = "Every 4 hours";
    public const string Every8H = "Every 8 hours";

    public static readonly IReadOnlyList<string> All =
    [
        TimeOfDay,
        Every5Min, Every15Min, Every30Min,
        Every1H, Every2H, Every4H, Every8H
    ];

    public static int? GetIntervalMinutes(string mode) => mode switch
    {
        Every5Min => 5,
        Every15Min => 15,
        Every30Min => 30,
        Every1H => 60,
        Every2H => 120,
        Every4H => 240,
        Every8H => 480,
        _ => null
    };
}

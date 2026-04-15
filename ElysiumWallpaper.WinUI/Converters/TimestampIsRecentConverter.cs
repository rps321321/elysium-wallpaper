using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ElysiumWallpaper.Converters;

/// <summary>
/// Returns Visible when the bound <see cref="DateTimeOffset"/> is after a shared
/// session-start sentinel held in <see cref="SessionThreshold"/>. Used for
/// "new since last launch" badges.
/// </summary>
public sealed class TimestampIsRecentConverter : IValueConverter
{
    public static DateTimeOffset SessionThreshold { get; set; } = DateTimeOffset.Now;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTimeOffset ts && ts >= SessionThreshold)
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

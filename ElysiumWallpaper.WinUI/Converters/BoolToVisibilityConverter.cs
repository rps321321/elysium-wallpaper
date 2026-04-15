using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ElysiumWallpaper.Converters;

/// <summary>
/// bool -> Visibility. ConverterParameter = "Invert" flips the result.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool v = value is bool b && b;
        bool invert = parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        return (v ^ invert) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

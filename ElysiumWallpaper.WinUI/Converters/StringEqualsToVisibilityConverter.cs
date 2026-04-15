using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ElysiumWallpaper.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound string equals the <c>ConverterParameter</c>
/// (case-insensitive), otherwise <see cref="Visibility.Collapsed"/>. Used to swap mutually-exclusive
/// panels based on a single selected-mode string.
/// </summary>
public sealed class StringEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string a = value as string ?? string.Empty;
        string b = parameter as string ?? string.Empty;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

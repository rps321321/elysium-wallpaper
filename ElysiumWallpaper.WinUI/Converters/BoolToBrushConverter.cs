using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace ElysiumWallpaper.Converters;

/// <summary>
/// Picks between two named brush resources depending on a bool value.
/// ConverterParameter format: "TrueBrushKey|FalseBrushKey".
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (parameter is not string spec || !spec.Contains('|')) return null;
        string[] parts = spec.Split('|', 2);
        string key = value is bool b && b ? parts[0] : parts[1];
        return Application.Current.Resources.TryGetValue(key, out object? res) ? res as Brush : null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

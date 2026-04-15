using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ElysiumWallpaper.Converters;

/// <summary>
/// Collapses a panel when the bound count is zero, and vice-versa when inverted.
/// ConverterParameter = "Invert" flips the logic so the panel is visible only when the list is empty.
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        int count = value switch
        {
            int i => i,
            ICollection c => c.Count,
            IEnumerable e => CountEnumerable(e),
            _ => 0
        };
        bool invert = parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        bool visible = invert ? count == 0 : count > 0;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();

    private static int CountEnumerable(IEnumerable e)
    {
        int n = 0;
        foreach (var _ in e) n++;
        return n;
    }
}

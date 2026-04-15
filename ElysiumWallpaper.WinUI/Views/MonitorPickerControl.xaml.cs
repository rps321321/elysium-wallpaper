using ElysiumWallpaper.Models;
using ElysiumWallpaper.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace ElysiumWallpaper.Views;

/// <summary>
/// #7: draws the user's monitor arrangement to scale. Clicking a monitor rect raises
/// <see cref="MonitorSelected"/> with the clicked display's DeviceName (or "ALL" when
/// clicking the empty canvas background).
/// </summary>
public sealed partial class MonitorPickerControl : UserControl
{
    public static readonly DependencyProperty DisplaysProperty =
        DependencyProperty.Register(
            nameof(Displays),
            typeof(IReadOnlyList<DisplayInfo>),
            typeof(MonitorPickerControl),
            new PropertyMetadata(null, OnDisplaysChanged));

    public static readonly DependencyProperty SelectedDeviceNameProperty =
        DependencyProperty.Register(
            nameof(SelectedDeviceName),
            typeof(string),
            typeof(MonitorPickerControl),
            new PropertyMetadata(WallpaperService.AllMonitors, OnSelectedChanged));

    public event EventHandler<string>? MonitorSelected;

    // Stored handlers so Unloaded can detach them. Subscribing in the constructor and
    // never unsubscribing leaks the control across page reloads (the canvas keeps a
    // delegate chain rooted to `this`).
    private TappedEventHandler? _canvasTapped;
    private SizeChangedEventHandler? _canvasSizeChanged;

    public MonitorPickerControl()
    {
        InitializeComponent();
        _canvasTapped = (_, _) =>
        {
            SelectedDeviceName = WallpaperService.AllMonitors;
            MonitorSelected?.Invoke(this, WallpaperService.AllMonitors);
            Redraw();
        };
        _canvasSizeChanged = (_, _) => Redraw();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LayoutCanvas.Tapped += _canvasTapped;
        LayoutCanvas.SizeChanged += _canvasSizeChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LayoutCanvas.Tapped -= _canvasTapped;
        LayoutCanvas.SizeChanged -= _canvasSizeChanged;
    }

    public IReadOnlyList<DisplayInfo>? Displays
    {
        get => (IReadOnlyList<DisplayInfo>?)GetValue(DisplaysProperty);
        set => SetValue(DisplaysProperty, value);
    }

    public string SelectedDeviceName
    {
        get => (string)GetValue(SelectedDeviceNameProperty);
        set => SetValue(SelectedDeviceNameProperty, value);
    }

    /// <summary>Forces the control to re-query each monitor's wallpaper and redraw.</summary>
    public void RefreshBackgrounds() => Redraw();

    private static void OnDisplaysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MonitorPickerControl)d).Redraw();

    private static void OnSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MonitorPickerControl)d).Redraw();

    private void Redraw()
    {
        try
        {
            RedrawCore();
        }
        catch
        {
            // Never let a decoder / layout glitch bubble up to WinUI and crash the app.
        }
    }

    private void RedrawCore()
    {
        LayoutCanvas.Children.Clear();
        var displays = Displays;
        if (displays is null || displays.Count == 0)
        {
            return;
        }

        // Compute bounding box in virtual screen coords, then scale into canvas space.
        int minX = displays.Min(d => d.PositionX);
        int minY = displays.Min(d => d.PositionY);
        int maxX = displays.Max(d => d.PositionX + d.Width);
        int maxY = displays.Max(d => d.PositionY + d.Height);

        double rangeX = Math.Max(1, maxX - minX);
        double rangeY = Math.Max(1, maxY - minY);
        double canvasW = Math.Max(300, LayoutCanvas.ActualWidth);
        double canvasH = LayoutCanvas.Height;

        const double Padding = 8;
        double scale = Math.Min(
            (canvasW - Padding * 2) / rangeX,
            (canvasH - Padding * 2) / rangeY);

        foreach (var display in displays)
        {
            double x = (display.PositionX - minX) * scale + Padding;
            double y = (display.PositionY - minY) * scale + Padding;
            double w = display.Width * scale;
            double h = display.Height * scale;

            bool isSelected = string.Equals(SelectedDeviceName, display.DeviceName, StringComparison.OrdinalIgnoreCase)
                              || (string.Equals(SelectedDeviceName, WallpaperService.AllMonitors, StringComparison.OrdinalIgnoreCase) && display.IsPrimary);

            var container = BuildMonitorVisual(display, w, h, isSelected);
            container.Tag = display.DeviceName;
            container.Tapped += OnRectTapped;

            Canvas.SetLeft(container, x);
            Canvas.SetTop(container, y);
            LayoutCanvas.Children.Add(container);

            var label = new TextBlock
            {
                Text = display.IsPrimary ? $"{display.FriendlyName} \u2605\n{display.Width}x{display.Height}"
                                         : $"{display.FriendlyName}\n{display.Width}x{display.Height}",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                IsHitTestVisible = false,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = Math.Max(20, w - 6)
            };
            Canvas.SetLeft(label, x + 6);
            Canvas.SetTop(label, y + 4);
            LayoutCanvas.Children.Add(label);
        }
    }

    private static FrameworkElement BuildMonitorVisual(DisplayInfo display, double w, double h, bool isSelected)
    {
        var root = new Grid
        {
            Width = w,
            Height = h
        };

        // Wallpaper background (per-monitor via IDesktopWallpaper, with fallback to system default).
        string? path = WallpaperService.GetWallpaperForMonitor(display.DeviceName) ?? WallpaperService.GetCurrent();
        var bgBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x1F, 0x2A, 0x44))
        };
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.ImageFailed += (_, _) => { /* swallow WIC decode failures */ };
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bgBorder.Background = new ImageBrush
                {
                    ImageSource = bmp,
                    Stretch = Stretch.UniformToFill
                };
            }
            catch
            {
                // Keep solid fallback on any synchronous failure.
            }
        }
        root.Children.Add(bgBorder);

        // Slight darken so the label stays readable on bright wallpapers.
        var tint = new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00))
        };
        root.Children.Add(tint);

        // Selection outline.
        var outline = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderBrush = new SolidColorBrush(isSelected ? Color.FromArgb(0xFF, 0x3F, 0x6F, 0xFF)
                                                         : Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(isSelected ? 2 : 1)
        };
        root.Children.Add(outline);

        return root;
    }

    private void OnRectTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: string deviceName })
        {
            SelectedDeviceName = deviceName;
            MonitorSelected?.Invoke(this, deviceName);
            Redraw();
        }
    }
}

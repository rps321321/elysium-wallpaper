using CommunityToolkit.Mvvm.Input;
using ElysiumWallpaper.ViewModels;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ElysiumWallpaper.Services;

/// <summary>
/// Owns the Windows shell tray icon, its context menu, and the "close = minimize to tray"
/// interception. Lifetime matches the app; cached on <see cref="App"/> so GC doesn't collect it.
/// </summary>
public sealed class TrayService
{
    private readonly Window _window;
    private readonly MainViewModel _vm;
    private TaskbarIcon? _icon;

    public TrayService(Window window, MainViewModel vm)
    {
        _window = window;
        _vm = vm;
    }

    public void Initialize()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "Elysium Wallpaper",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.targetsize-24_altform-unplated.png")),
            ContextFlyout = BuildMenu(),
            LeftClickCommand = new RelayCommand(ShowWindow)
        };
        _icon.ForceCreate();

        // Intercept the close button when the user opted into tray mode.
        var appWindow = _window.AppWindow;
        if (appWindow is not null)
        {
            appWindow.Closing += OnAppWindowClosing;
        }
    }

    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();

        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Show Elysium",
            Command = new RelayCommand(ShowWindow)
        });
        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Next wallpaper",
            Command = _vm.ShuffleCurrentSlotCommand
        });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Previous wallpaper",
            Command = _vm.UndoCommand
        });
        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Start engine",
            Command = _vm.StartEngineCommand
        });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Stop engine",
            Command = _vm.StopEngineCommand
        });
        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Exit",
            Command = new RelayCommand(ExitApp)
        });
        return menu;
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Only minimize-to-tray when the user enabled it; otherwise the close button works as expected.
        if (_vm.IsTrayEnabled)
        {
            args.Cancel = true;
            HideWindow();
        }
    }

    private void ShowWindow()
    {
        try
        {
            _window.Activate();
            var appWindow = _window.AppWindow;
            appWindow?.Show();
            if (appWindow?.Presenter is OverlappedPresenter overlapped)
            {
                // Restore if minimized.
                overlapped.Restore();
            }
        }
        catch (Exception ex) { EngineLog.Write($"tray.ShowWindow: {ex.Message}"); }
    }

    private void HideWindow()
    {
        try { _window.AppWindow?.Hide(); }
        catch (Exception ex) { EngineLog.Write($"tray.HideWindow: {ex.Message}"); }
    }

    private void ExitApp()
    {
        try
        {
            _icon?.Dispose();
            Application.Current.Exit();
        }
        catch (Exception ex) { EngineLog.Write($"tray.Exit: {ex.Message}"); }
    }

    public void Dispose()
    {
        try { _icon?.Dispose(); } catch (Exception ex) { EngineLog.Write($"tray.Dispose: {ex.Message}"); }
    }
}

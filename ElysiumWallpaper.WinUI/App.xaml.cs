using System.Text.Json;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ElysiumWallpaper
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window window = Window.Current;

        /// <summary>Returns the owning window for HWND interop (folder pickers etc.).</summary>
        public Window GetWindow() => window;
        private const string WindowBoundsFileName = "window-bounds.json";

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.UnhandledException += OnAppUnhandledException;
        }

        private void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // CrashReportService writes to %LocalAppData%\ElysiumWallpaper\crash-log.txt; the
            // next launch surfaces it in an InfoBar (see MainPage.OnLoaded).
            Services.CrashReportService.Append(e.Message, e.Exception);
            e.Handled = true;
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            window ??= new Window();
            window.Title = "Elysium Wallpaper";

            if (window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                window.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
            RestoreWindowBounds();
            SetWindowIcon();
            window.Closed += (_, _) => SaveWindowBounds();
            window.Activate();

            // Bring up the system tray icon once we know the window + its ViewModel exist.
            if (rootFrame.Content is MainPage page)
            {
                _trayService = new Services.TrayService(window, page.ViewModel);
                _trayService.Initialize();
            }
        }

        private Services.TrayService? _trayService;

        /// <summary>
        /// Sets the title-bar / taskbar icon. Multi-resolution .ico ships in Assets so Windows
        /// picks the right size for the title bar (16/24) and the alt-tab thumbnail (32/48).
        /// </summary>
        private void SetWindowIcon()
        {
            try
            {
                var appWindow = window.AppWindow;
                if (appWindow is null) return;
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
                if (File.Exists(iconPath)) appWindow.SetIcon(iconPath);
            }
            catch
            {
                // Cosmetic; never fail launch over an icon.
            }
        }

        private void RestoreWindowBounds()
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ElysiumWallpaper", WindowBoundsFileName);
                if (!File.Exists(path)) return;

                var bounds = JsonSerializer.Deserialize<WindowBoundsRecord>(File.ReadAllText(path));
                if (bounds is null || bounds.Width <= 0 || bounds.Height <= 0) return;

                var appWindow = window.AppWindow;
                if (appWindow is null) return;
                appWindow.MoveAndResize(new RectInt32(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
            }
            catch
            {
                // Best effort; ignore malformed state.
            }
        }

        private void SaveWindowBounds()
        {
            try
            {
                var appWindow = window.AppWindow;
                if (appWindow is null) return;
                var pos = appWindow.Position;
                var size = appWindow.Size;
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ElysiumWallpaper");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, WindowBoundsFileName);
                File.WriteAllText(path, JsonSerializer.Serialize(new WindowBoundsRecord
                {
                    Left = pos.X,
                    Top = pos.Y,
                    Width = size.Width,
                    Height = size.Height
                }));
            }
            catch
            {
            }
        }

        private sealed class WindowBoundsRecord
        {
            public int Left { get; set; }
            public int Top { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new InvalidOperationException(
                $"Failed to load page {e.SourcePageType.FullName}",
                e.Exception);
        }
    }
}

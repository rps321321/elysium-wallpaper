using ElysiumWallpaper.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;

namespace ElysiumWallpaper.Views;

public sealed partial class ImagePreviewDialog : ContentDialog
{
    public enum Action { None, Use, Favorite }
    public Action Result { get; private set; } = Action.None;

    // Stored handlers so Closed can detach them and let the dialog (+ its 4K-decoded
    // BitmapImage in WIC memory) be reclaimed instead of lingering on the GC heap.
    private TypedEventHandler<ContentDialog, ContentDialogButtonClickEventArgs>? _onPrimary;
    private TypedEventHandler<ContentDialog, ContentDialogButtonClickEventArgs>? _onSecondary;

    public ImagePreviewDialog(SearchResultViewModel result)
    {
        InitializeComponent();
        PreviewImage.Source = new BitmapImage(new Uri(result.DownloadUrl));
        PhotographerText.Text = result.Photographer;
        ResolutionText.Text = result.Resolution;

        _onPrimary = (_, _) => Result = Action.Use;
        _onSecondary = (_, _) => Result = Action.Favorite;
        PrimaryButtonClick += _onPrimary;
        SecondaryButtonClick += _onSecondary;
        Opened += OnDialogOpened;
        Closed += OnDialogClosed;
    }

    private void OnDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        // Size preview to ~85% of the hosting window so a 4K wallpaper gets real estate to breathe.
        if (XamlRoot is null) return;
        double targetW = XamlRoot.Size.Width * 0.85;
        double targetH = XamlRoot.Size.Height * 0.85;
        RootGrid.Width = targetW;
        RootGrid.Height = targetH - 120; // leave room for title + button row
    }

    private void OnDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        // Detach handlers + drop the BitmapImage so the WIC-decoded texture can be
        // reclaimed promptly. Without this, every preview leaks a 4K image until GC.
        if (_onPrimary is not null) PrimaryButtonClick -= _onPrimary;
        if (_onSecondary is not null) SecondaryButtonClick -= _onSecondary;
        Opened -= OnDialogOpened;
        Closed -= OnDialogClosed;
        PreviewImage.Source = null;
    }
}

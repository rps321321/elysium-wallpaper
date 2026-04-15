using ElysiumWallpaper.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ElysiumWallpaper.Views;

public sealed partial class ImagePreviewDialog : ContentDialog
{
    public enum Action { None, Use, Favorite }
    public Action Result { get; private set; } = Action.None;

    public ImagePreviewDialog(SearchResultViewModel result)
    {
        InitializeComponent();
        PreviewImage.Source = new BitmapImage(new Uri(result.DownloadUrl));
        PhotographerText.Text = result.Photographer;
        ResolutionText.Text = result.Resolution;

        PrimaryButtonClick += (_, _) => Result = Action.Use;
        SecondaryButtonClick += (_, _) => Result = Action.Favorite;
        Opened += OnDialogOpened;
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
}

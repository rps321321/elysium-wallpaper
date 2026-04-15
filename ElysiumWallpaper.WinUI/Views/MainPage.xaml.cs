using ElysiumWallpaper.Models;

namespace ElysiumWallpaper.Views
{
    public partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; }

        public MainPage()
        {
            ViewModel = new MainViewModel(this.DispatcherQueue);
            DataContext = ViewModel;
            this.InitializeComponent();
            Unloaded += OnUnloaded;
            Loaded += OnLoaded;
            ViewModel.PropertyChanged += async (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.SelectedTheme)) ApplyTheme();
                if (args.PropertyName == nameof(MainViewModel.CurrentImageFullPath))
                {
                    MonitorPicker?.RefreshBackgrounds();
                    await UpdateBackdropAsync();
                }
            };
        }

        private void Backdrop_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            // WIC can't decode the current wallpaper (e.g. HEIC without codec).
            if (sender is Image img) img.Source = null;
        }

        /// <summary>
        /// Every async-void event handler should go through here so its exceptions land in our
        /// status bar + engine log instead of taking the UI SynchronizationContext down.
        /// </summary>
        private async Task SafeAsync(string context, Func<Task> work)
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                ViewModel.StatusHeadline = $"{context} failed: {ex.Message}";
                ElysiumWallpaper.Services.EngineLog.Write($"handler '{context}' threw: {ex}");
            }
        }

        private async void OnLoaded(object sender, RoutedEventArgs e) => await SafeAsync("Page load", async () =>
        {
            ApplyTheme();
            ViewModel.DetectCurrentlyAppliedWallpaper();
            MonitorPicker?.RefreshBackgrounds();
            await UpdateBackdropAsync();
            ShowCrashBannerIfNeeded();
        });

        // ---- Crash recovery banner ----

        private void ShowCrashBannerIfNeeded()
        {
            if (ElysiumWallpaper.Services.CrashReportService.HasRecentCrash())
            {
                CrashInfoBar.IsOpen = true;
            }
        }

        private void OpenCrashLog_Click(object sender, RoutedEventArgs e)
        {
            ElysiumWallpaper.Services.CrashReportService.RevealInExplorer();
        }

        private void CopyCrashLog_Click(object sender, RoutedEventArgs e)
        {
            string log = ElysiumWallpaper.Services.CrashReportService.ReadLog();
            if (string.IsNullOrEmpty(log)) return;
            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(log);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
            ViewModel.StatusHeadline = "Crash log copied to clipboard.";
        }

        private void CrashInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        {
            // Banner dismissed (X button or programmatic). Archive the log so it doesn't reappear.
            ElysiumWallpaper.Services.CrashReportService.Dismiss();
        }

        private async Task UpdateBackdropAsync()
        {
            string path = ViewModel.CurrentImageFullPath;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                BackdropImage.Source = null;
                return;
            }

            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bmp.SetSourceAsync(stream);
                BackdropImage.Source = bmp;
            }
            catch
            {
                BackdropImage.Source = null;
            }
        }

        private void ApplyTheme()
        {
            RequestedTheme = ViewModel.SelectedTheme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsSidebarCollapsed = !ViewModel.IsSidebarCollapsed;
        }

        private async void PickUserFolder_Click(object sender, RoutedEventArgs e) => await SafeAsync("Pick folder", async () =>
        {
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add("*");

            // WinUI 3 desktop: the picker must be associated with the host window's HWND.
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current is App app ? app.GetWindow() : Window.Current);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) ViewModel.SetUserImagesFolder(folder.Path);
        });

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.Dispose();
        }

        private void UseSearchResult_Click(object sender, RoutedEventArgs e)
        {
            SearchResultViewModel? r = sender switch
            {
                Button { Tag: SearchResultViewModel r1 } => r1,
                MenuFlyoutItem { Tag: SearchResultViewModel r2 } => r2,
                _ => null
            };
            if (r is not null && ViewModel.UseSearchResultCommand.CanExecute(r))
            {
                ViewModel.UseSearchResultCommand.Execute(r);
            }
        }

        private void SaveFavorite_Click(object sender, RoutedEventArgs e)
        {
            SearchResultViewModel? r = sender switch
            {
                Button { Tag: SearchResultViewModel r1 } => r1,
                MenuFlyoutItem { Tag: SearchResultViewModel r2 } => r2,
                _ => null
            };
            if (r is not null && ViewModel.SaveFavoriteCommand.CanExecute(r))
            {
                ViewModel.SaveFavoriteCommand.Execute(r);
            }
        }

        private void ApplyFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: FavoriteItem favorite } && ViewModel.ApplyFavoriteCommand.CanExecute(favorite))
            {
                ViewModel.ApplyFavoriteCommand.Execute(favorite);
            }
        }

        private async void AddFavoriteToCollection_Click(object sender, RoutedEventArgs e) => await SafeAsync("Add to collection", async () =>
        {
            if (sender is not Button { Tag: FavoriteItem favorite }) return;

            string? target = await ChooseCollectionAsync(favorite);
            if (string.IsNullOrWhiteSpace(target)) return;

            if (!ViewModel.Collections.Any(c => string.Equals(c.Name, target, StringComparison.OrdinalIgnoreCase)))
            {
                ViewModel.CreateCollection(target);
            }

            ViewModel.SelectedCollectionName = target;
            ViewModel.AddFavoriteToCollectionByName(favorite, target);
        });

        private async Task<string?> ChooseCollectionAsync(FavoriteItem favorite)
        {
            bool hasExisting = ViewModel.Collections.Count > 0;

            var rows = new StackPanel { Spacing = 12 };
            rows.Children.Add(new TextBlock
            {
                Text = $"Add \"{favorite.Title}\" to a collection.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8
            });

            ComboBox? existingBox = null;
            if (hasExisting)
            {
                existingBox = new ComboBox
                {
                    Header = "Pick an existing collection",
                    PlaceholderText = "Choose one...",
                    ItemsSource = ViewModel.Collections,
                    DisplayMemberPath = "Name",
                    SelectedValuePath = "Name",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MinWidth = 320
                };
                rows.Children.Add(existingBox);
                rows.Children.Add(new TextBlock
                {
                    Text = "- or -",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Opacity = 0.5
                });
            }

            var nameBox = new TextBox
            {
                Header = hasExisting ? "Create a new collection" : "Name your first collection",
                PlaceholderText = "e.g. Mountains, Calm, Space"
            };
            rows.Children.Add(nameBox);

            var dialog = new ContentDialog
            {
                Title = hasExisting ? "Add to collection" : "Create a collection",
                Content = rows,
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                MinWidth = 420
            };

            string? chosen = null;
            dialog.PrimaryButtonClick += (_, args) =>
            {
                string typed = nameBox.Text?.Trim() ?? "";
                string? picked = existingBox?.SelectedValue as string;

                if (!string.IsNullOrWhiteSpace(typed))
                {
                    chosen = typed;
                }
                else if (!string.IsNullOrWhiteSpace(picked))
                {
                    chosen = picked;
                }
                else
                {
                    // Nothing filled in - keep the dialog open so the user sees the hint.
                    args.Cancel = true;
                }
            };

            await dialog.ShowAsync();
            return chosen;
        }

        private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: FavoriteItem favorite } && ViewModel.RemoveFavoriteCommand.CanExecute(favorite))
            {
                ViewModel.RemoveFavoriteCommand.Execute(favorite);
            }
        }

        private void ApplyHistory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: HistoryItem item } && ViewModel.ApplyHistoryCommand.CanExecute(item))
            {
                ViewModel.ApplyHistoryCommand.Execute(item);
            }
        }

        private void SearchViewSelector_SelectionChanged(Microsoft.UI.Xaml.Controls.SelectorBar sender, Microsoft.UI.Xaml.Controls.SelectorBarSelectionChangedEventArgs args)
        {
            var selected = sender.SelectedItem;
            if (selected?.Tag is string mode)
            {
                ViewModel.SelectedSearchView = mode;
            }
        }

        private void LibraryTabSelector_SelectionChanged(Microsoft.UI.Xaml.Controls.SelectorBar sender, Microsoft.UI.Xaml.Controls.SelectorBarSelectionChangedEventArgs args)
        {
            if (sender.SelectedItem?.Tag is string tab)
            {
                ViewModel.SelectedLibraryTab = tab;
            }
        }

        private void MonitorPicker_Selected(object? sender, string deviceName)
        {
            ViewModel.SelectedMonitorId = deviceName;
        }

        // ---- Page-level keyboard accelerators (registered in MainPage.xaml) ----

        /// <summary>Ctrl+K — focus the tag search box.</summary>
        private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SearchBox?.Focus(FocusState.Programmatic);
            args.Handled = true;
        }

        /// <summary>Ctrl+R — reroll the current slot wallpaper.</summary>
        private void Reroll_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (ViewModel.ShuffleCurrentSlotCommand.CanExecute(null))
                ViewModel.ShuffleCurrentSlotCommand.Execute(null);
            args.Handled = true;
        }

        /// <summary>F5 — fetch fresh wallpapers and apply the current slot.</summary>
        private void FetchNow_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (ViewModel.FetchNowCommand.CanExecute(null))
                ViewModel.FetchNowCommand.Execute(null);
            args.Handled = true;
        }

        private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (ViewModel.SearchByTagCommand.CanExecute(null))
                {
                    ViewModel.SearchByTagCommand.Execute(null);
                }
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                ViewModel.SearchTag = string.Empty;
                e.Handled = true;
            }
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (!string.IsNullOrWhiteSpace(args.QueryText))
            {
                ViewModel.SearchTag = args.QueryText;
                if (ViewModel.SearchByTagCommand.CanExecute(null))
                {
                    ViewModel.SearchByTagCommand.Execute(null);
                }
            }
        }

        private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            ViewModel.SearchTag = args.SelectedItem?.ToString() ?? ViewModel.SearchTag;
        }

        private void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            string? url = sender switch
            {
                Button { Tag: SearchResultViewModel r } => r.DownloadUrl,
                MenuFlyoutItem { Tag: SearchResultViewModel r2 } => r2.DownloadUrl,
                _ => null
            };
            if (string.IsNullOrWhiteSpace(url)) return;
            var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
            pkg.SetText(url);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        }

        private async void OpenOnPexels_Click(object sender, RoutedEventArgs e) => await SafeAsync("Open on Pexels", async () =>
        {
            string? url = sender switch
            {
                Button { Tag: SearchResultViewModel r } => r.DownloadUrl,
                MenuFlyoutItem { Tag: SearchResultViewModel r2 } => r2.DownloadUrl,
                _ => null
            };
            if (string.IsNullOrWhiteSpace(url)) return;
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        });

        private async void SearchItem_ItemClick(object sender, ItemClickEventArgs e) => await SafeAsync("Preview image", async () =>
        {
            if (MultiSelectToggle?.IsChecked == true) return;
            if (sender is GridView gv && gv.SelectionMode != ListViewSelectionMode.None) return;
            if (e.ClickedItem is SearchResultViewModel result)
            {
                await OpenPreviewDialogAsync(result);
            }
        });

        private async Task OpenPreviewDialogAsync(SearchResultViewModel result)
        {
            var dialog = new ImagePreviewDialog(result) { XamlRoot = this.XamlRoot };
            await dialog.ShowAsync();
            if (dialog.Result == ImagePreviewDialog.Action.Use && ViewModel.UseSearchResultCommand.CanExecute(result))
            {
                ViewModel.UseSearchResultCommand.Execute(result);
            }
            else if (dialog.Result == ImagePreviewDialog.Action.Favorite && ViewModel.SaveFavoriteCommand.CanExecute(result))
            {
                ViewModel.SaveFavoriteCommand.Execute(result);
            }
        }

        private async void SearchCard_Click(object sender, RoutedEventArgs e) => await SafeAsync("Open preview", async () =>
        {
            SearchResultViewModel? result = sender switch
            {
                Button { Tag: SearchResultViewModel r } => r,
                MenuFlyoutItem { Tag: SearchResultViewModel r2 } => r2,
                _ => null
            };
            if (result is null) return;
            var dialog = new ImagePreviewDialog(result) { XamlRoot = this.XamlRoot };
            await dialog.ShowAsync();
            if (dialog.Result == ImagePreviewDialog.Action.Use && ViewModel.UseSearchResultCommand.CanExecute(result))
            {
                ViewModel.UseSearchResultCommand.Execute(result);
            }
            else if (dialog.Result == ImagePreviewDialog.Action.Favorite && ViewModel.SaveFavoriteCommand.CanExecute(result))
            {
                ViewModel.SaveFavoriteCommand.Execute(result);
            }
        });

        /// <summary>
        /// Stretches GridView items to fill the row: picks the number of columns that fits the
        /// minimum card width (from the GridView's Tag), then divides the available width evenly.
        /// Eliminates the trailing wrap-remainder gap that fixed-width cards produce.
        /// </summary>
        private void ResponsiveGrid_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
        {
            if (sender is not Microsoft.UI.Xaml.Controls.GridView gv) return;
            if (gv.ItemsPanelRoot is not Microsoft.UI.Xaml.Controls.ItemsWrapGrid panel) return;

            double minCardWidth = 240;
            if (gv.Tag is string tag && double.TryParse(tag, out var parsed))
            {
                minCardWidth = parsed;
            }

            double available = e.NewSize.Width;
            if (available <= 0) return;

            int cols = Math.Max(1, (int)(available / minCardWidth));
            panel.ItemWidth = Math.Floor(available / cols);
        }

        private void SearchGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (sender is GridView gv && gv.SelectionMode == ListViewSelectionMode.Multiple)
                {
                    ViewModel.SelectedResultsCount = gv.SelectedItems.Count;
                    ViewModel.IsBulkSelectEnabled = gv.SelectedItems.Count > 0;
                }
            }
            catch
            {
                // Selection events can fire during mode transitions; ignore stale dispatch.
            }
        }

        private void MultiSelectToggle_Click(object sender, RoutedEventArgs e)
        {
            bool on = MultiSelectToggle.IsChecked == true;

            try
            {
                SearchGridView.SelectedItems.Clear();

                // Flip IsItemClickEnabled first so WinUI renders the correct chrome for the
                // new mode - item-click mode suppresses the multi-select check mark overlay.
                if (on)
                {
                    SearchGridView.IsItemClickEnabled = false;
                    SearchGridView.SelectionMode = ListViewSelectionMode.Multiple;
                }
                else
                {
                    SearchGridView.SelectionMode = ListViewSelectionMode.None;
                    SearchGridView.IsItemClickEnabled = true;
                }

                ViewModel.IsBulkSelectEnabled = on;
                ViewModel.SelectedResultsCount = 0;
            }
            catch (Exception ex)
            {
                ViewModel.StatusHeadline = $"Multi-select toggle failed: {ex.Message}";
            }
        }

        private async void BulkFavorite_Click(object sender, RoutedEventArgs e) => await SafeAsync("Bulk favorite", async () =>
        {
            var items = SearchGridView.SelectedItems.OfType<SearchResultViewModel>().ToList();
            foreach (var r in items)
            {
                if (ViewModel.SaveFavoriteCommand.CanExecute(r))
                {
                    await ((IAsyncRelayCommand)ViewModel.SaveFavoriteCommand).ExecuteAsync(r);
                }
            }
            SearchGridView.SelectedItems.Clear();
        });

        private void BulkClear_Click(object sender, RoutedEventArgs e)
        {
            SearchGridView.SelectedItems.Clear();
        }

        private void JumpToPage_Click(object sender, RoutedEventArgs e)
        {
            int target = (int)JumpPageBox.Value;
            if (target >= 1 && target <= ViewModel.SearchTotalPages && target != ViewModel.SearchPage)
            {
                ViewModel.SearchPage = target;
                _ = ViewModel.JumpToSearchPageAsync();
            }
        }

        private const string FavoriteDragFormat = "elysium/favorite-id";

        private void FavoritesGrid_DragItemsStarting(object sender, Microsoft.UI.Xaml.Controls.DragItemsStartingEventArgs e)
        {
            if (e.Items.Count == 1 && e.Items[0] is FavoriteItem fav)
            {
                e.Data.SetData(FavoriteDragFormat, fav.Id);
                e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Link;
            }
        }

        private void CollectionRow_DragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
        {
            if (e.DataView.Contains(FavoriteDragFormat))
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Link;
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.Caption = "Add to collection";
            }
        }

        private async void CollectionRow_Drop(object sender, Microsoft.UI.Xaml.DragEventArgs e) => await SafeAsync("Drop into collection", async () =>
        {
            if (sender is not FrameworkElement { Tag: CollectionItem collection }) return;
            if (!e.DataView.Contains(FavoriteDragFormat)) return;

            var deferral = e.GetDeferral();
            try
            {
                object raw = await e.DataView.GetDataAsync(FavoriteDragFormat);
                if (raw is string favoriteId)
                {
                    var fav = ViewModel.Favorites.FirstOrDefault(f => f.Id == favoriteId);
                    if (fav is not null)
                    {
                        ViewModel.AddFavoriteToCollectionByName(fav, collection.Name);
                    }
                }
            }
            finally
            {
                deferral.Complete();
            }
        });

        private async void RenameCollection_Click(object sender, RoutedEventArgs e) => await SafeAsync("Rename collection", async () =>
        {
            if (sender is not Button { Tag: CollectionItem c }) return;

            var input = new TextBox { Text = c.Name };
            var dialog = new ContentDialog
            {
                Title = "Rename collection",
                Content = input,
                PrimaryButtonText = "Rename",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.RenameCollection(c, input.Text);
            }
        });

        private async void DeleteCollection_Click(object sender, RoutedEventArgs e) => await SafeAsync("Delete collection", async () =>
        {
            if (sender is not Button { Tag: CollectionItem c }) return;

            var dialog = new ContentDialog
            {
                Title = "Delete collection?",
                Content = $"This removes \"{c.Name}\" ({c.FavoriteIds.Count} items). Your favorites remain.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                ViewModel.DeleteCollection(c);
            }
        });

        private void PrevFavoritesPage_Click(object sender, RoutedEventArgs e) => ViewModel.PagedFavorites.PrevPage();
        private void NextFavoritesPage_Click(object sender, RoutedEventArgs e) => ViewModel.PagedFavorites.NextPage();
        private void PrevHistoryPage_Click(object sender, RoutedEventArgs e) => ViewModel.PagedHistory.PrevPage();
        private void NextHistoryPage_Click(object sender, RoutedEventArgs e) => ViewModel.PagedHistory.NextPage();
    }
}

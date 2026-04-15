using ElysiumWallpaper.Models;

namespace ElysiumWallpaper.ViewModels;

/// <summary>
/// Library concerns: favorites, history, collections, plus filesystem-level housekeeping
/// (legacy library migration, path relocation for moved images).
/// </summary>
public sealed partial class MainViewModel
{
    [RelayCommand]
    private void ApplyFavorite(FavoriteItem? favorite)
    {
        if (favorite is null || !File.Exists(favorite.ImagePath))
        {
            return;
        }

        ApplyAndTrack(favorite.ImagePath);
    }

    [RelayCommand]
    private void RemoveFavorite(FavoriteItem? favorite)
    {
        if (favorite is null)
        {
            return;
        }

        Favorites.Remove(favorite);
        foreach (var collection in Collections)
        {
            collection.FavoriteIds.Remove(favorite.Id);
        }
        SaveProfile();
    }

    [RelayCommand]
    private void Undo()
    {
        if (History.Count < 2)
        {
            StatusHeadline = "No previous wallpaper to restore.";
            return;
        }

        var previous = History[1];
        if (File.Exists(previous.ImagePath))
        {
            ApplyAndTrack(previous.ImagePath);
            StatusHeadline = "Rolled back to previous wallpaper.";
        }
    }

    [RelayCommand]
    private void ApplyHistory(HistoryItem? historyItem)
    {
        if (historyItem is null || !File.Exists(historyItem.ImagePath))
        {
            return;
        }
        ApplyAndTrack(historyItem.ImagePath);
    }

    public void CreateCollection(string newName)
    {
        NewCollectionName = newName ?? string.Empty;
        CreateCollectionFromCurrentName();
    }

    private void CreateCollectionFromCurrentName()
    {
        string name = NewCollectionName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        if (Collections.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Collections.Add(new CollectionItem { Name = name });
        SelectedCollectionName = name;
        NewCollectionName = string.Empty;
        SaveProfile();
    }

    public void AddFavoriteToCollectionByName(FavoriteItem favorite, string collectionName)
    {
        var collection = Collections.FirstOrDefault(c => c.Name == collectionName);
        if (collection is null || collection.FavoriteIds.Contains(favorite.Id)) return;
        collection.FavoriteIds.Add(favorite.Id);
        SaveProfile();
        StatusHeadline = $"Added \"{favorite.Title}\" to \"{collectionName}\".";
    }

    public void DeleteCollection(CollectionItem collection)
    {
        Collections.Remove(collection);
        if (SelectedCollectionName == collection.Name) SelectedCollectionName = "";
        SaveProfile();
    }

    public void RenameCollection(CollectionItem collection, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == collection.Name) return;
        if (Collections.Any(c => string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase))) return;
        string old = collection.Name;
        collection.Name = newName;
        if (SelectedCollectionName == old) SelectedCollectionName = newName;
        SaveProfile();
        StatusHeadline = $"Renamed to \"{newName}\".";
    }

    [RelayCommand]
    private void AddFavoriteToCollection(FavoriteItem? favorite)
    {
        if (favorite is null) return;

        if (string.IsNullOrWhiteSpace(SelectedCollectionName))
        {
            StatusHeadline = "Pick a target collection first (dropdown next to the filter).";
            return;
        }

        var collection = Collections.FirstOrDefault(c => c.Name == SelectedCollectionName);
        if (collection is null)
        {
            StatusHeadline = $"Collection \"{SelectedCollectionName}\" is gone.";
            return;
        }
        if (collection.FavoriteIds.Contains(favorite.Id))
        {
            StatusHeadline = $"\"{favorite.Title}\" is already in \"{collection.Name}\".";
            return;
        }

        collection.FavoriteIds.Add(favorite.Id);
        SaveProfile();
        StatusHeadline = $"Added \"{favorite.Title}\" to \"{collection.Name}\".";
    }

    [RelayCommand]
    private void ApplyNextInCollection()
    {
        if (string.IsNullOrWhiteSpace(SelectedCollectionName))
        {
            StatusHeadline = "Select a collection first.";
            return;
        }

        var collection = Collections.FirstOrDefault(c => c.Name == SelectedCollectionName);
        if (collection is null || collection.FavoriteIds.Count == 0)
        {
            StatusHeadline = "Collection has no favorites.";
            return;
        }

        if (collection.NextIndex >= collection.FavoriteIds.Count)
        {
            collection.NextIndex = 0;
        }

        string id = collection.FavoriteIds[collection.NextIndex];
        collection.NextIndex++;
        var favorite = Favorites.FirstOrDefault(f => f.Id == id);
        if (favorite is not null && File.Exists(favorite.ImagePath))
        {
            ApplyAndTrack(favorite.ImagePath);
            SaveProfile();
        }
    }

    /// <summary>
    /// One-time copy of any images from the old in-bin library folder into the persistent
    /// LocalAppData location, then rewrites profile entries so their ImagePath points to
    /// the new location. Safe to call repeatedly.
    /// </summary>
    private void MigrateLegacyLibrary()
    {
        string oldLibrary = Path.Combine(_appBasePath, "library");
        if (!Directory.Exists(oldLibrary)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(oldLibrary))
            {
                string dest = Path.Combine(_libraryPath, Path.GetFileName(file));
                if (!File.Exists(dest))
                {
                    File.Copy(file, dest, overwrite: false);
                }
            }
        }
        catch
        {
            // Best-effort migration; broken files just stay missing.
        }
    }

    /// <summary>
    /// Walks favorites + history; if their ImagePath is gone, tries to resolve the file by
    /// name in the new library location and rewrites the path.
    /// </summary>
    private void RelocateMissingImages()
    {
        bool changed = false;
        foreach (var fav in Favorites)
        {
            if (!File.Exists(fav.ImagePath) && !string.IsNullOrWhiteSpace(fav.FileName))
            {
                string candidate = Path.Combine(_libraryPath, fav.FileName);
                if (File.Exists(candidate))
                {
                    fav.ImagePath = candidate;
                    changed = true;
                }
            }
        }
        foreach (var h in History)
        {
            if (!File.Exists(h.ImagePath) && !string.IsNullOrWhiteSpace(h.FileName))
            {
                string candidate = Path.Combine(_libraryPath, h.FileName);
                if (File.Exists(candidate))
                {
                    h.ImagePath = candidate;
                    changed = true;
                }
            }
        }
        if (changed)
        {
            SaveProfile();
            // Force PagedView to refresh visuals with the corrected paths.
            RebuildFilteredFavorites();
        }
    }

    private void AddFavorite(string filePath, string title, string sourceUrl)
    {
        if (Favorites.Any(f => string.Equals(f.ImagePath, filePath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Favorites.Insert(0, new FavoriteItem
        {
            Title = title,
            ImagePath = filePath,
            FileName = Path.GetFileName(filePath),
            SourceUrl = sourceUrl,
            SavedAt = DateTimeOffset.Now
        });
    }

    private void AddHistory(string path, string origin = "user")
    {
        if (History.Count > 0 && string.Equals(History[0].ImagePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        History.Insert(0, new HistoryItem
        {
            ImagePath = path,
            FileName = Path.GetFileName(path),
            AppliedAt = DateTimeOffset.Now,
            AppliedBy = origin
        });
        while (History.Count > 50)
        {
            History.RemoveAt(History.Count - 1);
        }
    }

    private void RebuildFilteredFavorites()
    {
        // Skip during bulk load (LoadProfile clears + adds N favorites one-at-a-time,
        // each Add fires CollectionChanged → RebuildFilteredFavorites — that's O(N²)
        // sort/filter work for a 200-favorite profile). LoadProfile calls this once
        // at the end after _isLoadingProfile flips back to false.
        if (_isLoadingProfile) return;

        IEnumerable<FavoriteItem> q = Favorites;
        if (!string.IsNullOrWhiteSpace(FavoritesFilter))
        {
            string needle = FavoritesFilter.Trim();
            q = q.Where(f =>
                (f.Title?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (f.FileName?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        q = FavoritesSort switch
        {
            "Oldest" => q.OrderBy(f => f.SavedAt),
            "Title" => q.OrderBy(f => f.Title, StringComparer.OrdinalIgnoreCase),
            _ => q.OrderByDescending(f => f.SavedAt)
        };

        FilteredFavorites.Clear();
        foreach (var f in q) FilteredFavorites.Add(f);
    }

    partial void OnFavoritesFilterChanged(string value) { RebuildFilteredFavorites(); SaveProfile(); }
    partial void OnFavoritesSortChanged(string value) { RebuildFilteredFavorites(); SaveProfile(); }
}

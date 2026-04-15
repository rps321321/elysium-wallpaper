using ElysiumWallpaper.Services;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ElysiumWallpaper.ViewModels;

/// <summary>
/// Search + pagination concerns for the main VM: Pexels querying, buffered filtering,
/// prefetch, and the "use/save" actions that hand a search result off to the library.
/// </summary>
public sealed partial class MainViewModel
{
    [RelayCommand]
    private Task SearchByTagAsync()
    {
        RememberTag(SearchTag);
        HasSearchError = false;
        ResetSearchState();
        SearchPage = 1;
        return LoadSearchPageAsync();
    }

    private void RememberTag(string tag)
    {
        string trimmed = tag?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed)) return;
        RecentTags.Remove(trimmed);
        RecentTags.Insert(0, trimmed);
        while (RecentTags.Count > 10) RecentTags.RemoveAt(RecentTags.Count - 1);
        SaveProfile();
    }

    private void ResetSearchState()
    {
        _filteredBuffer.Clear();
        _seenPhotoIds.Clear();
        _pexelsPagesFetched = 0;
        _pexelsTotalResults = 0;
        _cachedSearchSignature = BuildSearchSignature();
    }

    private string BuildSearchSignature()
        => $"{SearchTag.Trim()}|{SelectedOrientation}|{SelectedColorTone}|{MinWidth}x{MinHeight}|{MatchScreenAspect}|{AspectTolerance}";

    partial void OnMatchScreenAspectChanged(bool value)
    {
        if (!_isLoadingProfile) { _ = SearchByTagAsync(); }
    }

    [RelayCommand]
    private Task NextSearchPageAsync()
    {
        if (!CanGoNextSearchPage) return Task.CompletedTask;
        SearchPage++;
        return LoadSearchPageAsync();
    }

    [RelayCommand]
    private Task PrevSearchPageAsync()
    {
        if (!CanGoPrevSearchPage) return Task.CompletedTask;
        SearchPage--;
        return LoadSearchPageAsync();
    }

    public Task JumpToSearchPageAsync() => LoadSearchPageAsync();

    private async Task LoadSearchPageAsync()
    {
        string tag = SearchTag.Trim();
        if (string.IsNullOrWhiteSpace(tag))
        {
            SearchStatus = "Enter a tag first.";
            return;
        }

        // Wait for any in-flight prefetch to land before we start mutating shared state.
        // Otherwise SearchResults.Add(_filteredBuffer[i]) below could race with the BG
        // _filteredBuffer.Add(...) and throw IndexOutOfRange.
        await _searchGate.WaitAsync();
        try
        {
            // Filters drifted - reset buffer + dedup set so we refetch cleanly.
            string signature = BuildSearchSignature();
            if (!string.Equals(_cachedSearchSignature, signature, StringComparison.Ordinal))
            {
                ResetSearchState();
            }

            SearchResults.Clear();

            // Ensure the buffer holds enough items to cover the requested UI page.
            int needed = SearchPage * DisplayPageSize;
            try
            {
                IsSearching = true;
                while (_filteredBuffer.Count < needed && HasMorePexelsPages())
                {
                    SearchStatus = $"Searching \"{tag}\" (buffered {_filteredBuffer.Count})...";
                    bool fetched = await FetchNextPexelsBatchAsync(tag, signature);
                    if (!fetched) break;
                }
            }
            catch (Exception ex)
            {
                SearchStatus = $"Search failed: {ex.Message}";
                HasSearchError = true;
                LastSearchError = ex.Message;
                IsSearching = false;
                return;
            }

            // Slice the buffer for the current UI page.
            int start = (SearchPage - 1) * DisplayPageSize;
            int end = Math.Min(start + DisplayPageSize, _filteredBuffer.Count);
            for (int i = start; i < end; i++)
            {
                SearchResults.Add(_filteredBuffer[i]);
            }

            // Compute an estimate of total filtered pages from observed survival rate.
            UpdateEstimatedTotals();

            SearchStatus = SearchResults.Count == 0
                ? $"No more images for \"{tag}\"."
                : $"Page {SearchPage} of {SearchTotalPages} (~{_pexelsTotalResults:N0})";
        }
        finally
        {
            _searchGate.Release();
        }

        // Kick off the next-page prefetch AFTER releasing the gate so it can acquire it.
        TryPrefetchNextPage(tag);
        IsSearching = false;
        return;

        // ----- local helpers kept below for clarity -----
        bool HasMorePexelsPages()
        {
            if (_pexelsTotalResults == 0) return true; // haven't asked yet
            return _pexelsPagesFetched * PexelsFetchSize < _pexelsTotalResults;
        }
    }

    private async Task<bool> FetchNextPexelsBatchAsync(string tag, string signature)
    {
        // Tag-based search is Pexels-only (Openverse has no equivalent paginated tag API
        // that we use). Without a key the feature simply isn't available — surface that
        // clearly rather than firing an authless request that returns 401.
        string? apiKey = PexelsKeyProvider.Key;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SearchStatus = "Tag search needs a free Pexels API key. See README → 'Tag search'.";
            return false;
        }

        int pexelsPage = _pexelsPagesFetched + 1;
        string encoded = Uri.EscapeDataString(tag);
        var query = new List<string>
        {
            $"query={encoded}",
            $"per_page={PexelsFetchSize}",
            $"page={pexelsPage}"
        };
        if (!string.Equals(SelectedOrientation, "Any", StringComparison.OrdinalIgnoreCase))
            query.Add($"orientation={SelectedOrientation}");
        if (!string.Equals(SelectedColorTone, "Any", StringComparison.OrdinalIgnoreCase))
            query.Add($"color={SelectedColorTone}");
        string url = $"https://api.pexels.com/v1/search?{string.Join("&", query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", apiKey);
        request.Headers.UserAgent.ParseAdd("ElysiumWallpaper/1.0");

        using HttpResponseMessage response = await SearchClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            SearchStatus = $"Search failed: {(int)response.StatusCode} ({response.ReasonPhrase}).";
            return false;
        }

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = document.RootElement;

        // Only honor the response if the signature hasn't drifted mid-flight.
        if (!string.Equals(_cachedSearchSignature, signature, StringComparison.Ordinal))
        {
            return false;
        }

        if (root.TryGetProperty("total_results", out JsonElement totalEl) && totalEl.ValueKind == JsonValueKind.Number)
        {
            _pexelsTotalResults = totalEl.GetInt32();
        }

        if (!root.TryGetProperty("photos", out JsonElement photos) || photos.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement photo in photos.EnumerateArray())
        {
            if (!photo.TryGetProperty("src", out JsonElement src)) continue;

            long photoId = photo.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.Number
                ? idEl.GetInt64() : 0;
            if (photoId != 0 && !_seenPhotoIds.Add(photoId)) continue;

            int width = photo.TryGetProperty("width", out JsonElement w) ? w.GetInt32() : 0;
            int height = photo.TryGetProperty("height", out JsonElement h) ? h.GetInt32() : 0;
            if (width < MinWidth || height < MinHeight) continue;

            // Aspect-ratio filter: drop photos whose shape is far from the monitor's.
            if (MatchScreenAspect && !AspectRatioMatchesScreen(width, height)) continue;

            bool hiDpi = SystemSpecs.Displays.FirstOrDefault(d => d.IsPrimary)?.Width >= 2560;
            string preview = hiDpi && src.TryGetProperty("large2x", out JsonElement large2x)
                ? (large2x.GetString() ?? string.Empty)
                : (src.TryGetProperty("large", out JsonElement large) ? (large.GetString() ?? string.Empty) : string.Empty);
            string download = src.TryGetProperty("original", out JsonElement original) ? (original.GetString() ?? string.Empty) : preview;
            string photographer = photo.TryGetProperty("photographer", out JsonElement p) ? (p.GetString() ?? "Unknown") : "Unknown";
            if (!string.IsNullOrWhiteSpace(preview) && !string.IsNullOrWhiteSpace(download))
            {
                _filteredBuffer.Add(new SearchResultViewModel(tag, preview, download, width, height, photographer));
            }
        }

        _pexelsPagesFetched = pexelsPage;
        return true;
    }

    private bool AspectRatioMatchesScreen(int photoW, int photoH)
    {
        var primary = SystemSpecs.Displays.FirstOrDefault(d => d.IsPrimary)
                      ?? SystemSpecs.Displays.FirstOrDefault();
        if (primary is null || primary.Width <= 0 || primary.Height <= 0) return true;
        if (photoW <= 0 || photoH <= 0) return false;

        double screenRatio = primary.Width / (double)primary.Height;
        double photoRatio = photoW / (double)photoH;
        double diff = Math.Abs(photoRatio - screenRatio) / screenRatio;
        return diff <= AspectTolerance;
    }

    private void UpdateEstimatedTotals()
    {
        if (_pexelsPagesFetched == 0 || _pexelsTotalResults == 0)
        {
            SearchTotalResults = _filteredBuffer.Count;
            SearchTotalPages = Math.Max(1, (int)Math.Ceiling(_filteredBuffer.Count / (double)DisplayPageSize));
        }
        else
        {
            // Extrapolate survival rate from the fetches we've done so far.
            int fetchedSoFar = _pexelsPagesFetched * PexelsFetchSize;
            double survivalRate = _filteredBuffer.Count / (double)Math.Max(1, fetchedSoFar);
            int estimatedFiltered = (int)Math.Ceiling(_pexelsTotalResults * survivalRate);
            SearchTotalResults = Math.Max(estimatedFiltered, _filteredBuffer.Count);
            SearchTotalPages = Math.Max(1, (int)Math.Ceiling(SearchTotalResults / (double)DisplayPageSize));
        }
        UpdatePaginationFlags();
    }

    /// <summary>
    /// #8 - when memory is ample and the buffer may be short for the next UI page,
    /// kick off another Pexels batch in the background so Next is instant.
    /// </summary>
    private void TryPrefetchNextPage(string tag)
    {
        const ulong FourGb = 4UL * 1024 * 1024 * 1024;
        if (SystemSpecs.Memory.AvailableBytes < FourGb) return;

        int nextDisplayEnd = (SearchPage + 1) * DisplayPageSize;
        if (_filteredBuffer.Count >= nextDisplayEnd) return;
        if (_pexelsTotalResults > 0 && _pexelsPagesFetched * PexelsFetchSize >= _pexelsTotalResults) return;

        string signature = BuildSearchSignature();
        _ = Task.Run(async () =>
        {
            // Serialize with foreground LoadSearchPageAsync — both mutate _filteredBuffer
            // / _seenPhotoIds / _pexelsPagesFetched, which are plain non-thread-safe
            // collections. Without the gate we hit IndexOutOfRange / corruption.
            await _searchGate.WaitAsync();
            try
            {
                await FetchNextPexelsBatchAsync(tag, signature);
            }
            catch
            {
                // Prefetch is best-effort.
            }
            finally
            {
                _searchGate.Release();
            }
        });
    }

    private void UpdatePaginationFlags()
    {
        CanGoPrevSearchPage = SearchPage > 1;
        CanGoNextSearchPage = SearchPage < SearchTotalPages;
        SearchPageLabel = SearchTotalResults == 0
            ? ""
            : $"Page {SearchPage} of {SearchTotalPages}  ({SearchTotalResults:N0} results)";
    }

    partial void OnSearchPageChanged(int value) => UpdatePaginationFlags();
    partial void OnSearchTotalPagesChanged(int value) => UpdatePaginationFlags();

    [RelayCommand]
    private async Task UseSearchResultAsync(SearchResultViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        try
        {
            StatusHeadline = "Applying selected image...";
            string filePath = await DownloadToLibraryAsync(result);
            ApplyAndTrack(filePath);
            SearchStatus = "Selected image saved and applied.";
        }
        catch (Exception ex)
        {
            SearchStatus = $"Could not use selected image: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveFavoriteAsync(SearchResultViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        string path = await DownloadToLibraryAsync(result);
        AddFavorite(path, result.Tag, result.DownloadUrl);
        SaveProfile();
        StatusHeadline = "Added to favorites.";
    }

    private async Task<string> DownloadToLibraryAsync(SearchResultViewModel result)
    {
        Directory.CreateDirectory(_libraryPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, result.DownloadUrl);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        using HttpResponseMessage response = await SearchClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string extension = PexelsClient.DetectExtension(result.DownloadUrl);
        string fileName = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{extension}";
        string targetPath = Path.Combine(_libraryPath, fileName);

        await using (var stream = await response.Content.ReadAsStreamAsync())
        await using (var output = File.Create(targetPath))
        {
            await stream.CopyToAsync(output);
        }

        return targetPath;
    }
}

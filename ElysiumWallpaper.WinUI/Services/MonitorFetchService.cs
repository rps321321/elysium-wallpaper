using System.Net.Http.Headers;
using System.Text.Json;
using ElysiumWallpaper.Models;

namespace ElysiumWallpaper.Services;

/// <summary>
/// #2: fetches one Pexels image per physical monitor, picking orientation + size hints from each
/// monitor's native resolution, then writes each chosen image to the library and returns the
/// monitor -> image path map for applying via IDesktopWallpaper.
/// </summary>
public static class MonitorFetchService
{
    public static async Task<IReadOnlyDictionary<string, string>> FetchOneImagePerMonitorAsync(
        HttpClient client,
        string apiKey,
        string tag,
        string libraryPath,
        IReadOnlyList<DisplayInfo> displays,
        CancellationToken cancellationToken,
        int maxParallel = 4)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (displays.Count == 0 || string.IsNullOrWhiteSpace(tag))
        {
            return result;
        }

        Directory.CreateDirectory(libraryPath);

        // #5 - cap parallelism based on caller's CPU budget, monitor count, and a ceiling for Pexels politeness.
        int degree = Math.Max(1, Math.Min(maxParallel, displays.Count));
        using var gate = new SemaphoreSlim(degree);
        var lockObj = new object();

        var tasks = displays.Select(async display =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string orientation = ClassifyOrientation(display);
                string url = BuildSearchUrl(tag, orientation, display.Width, display.Height);

                string? downloadUrl = null;

                // Try Pexels first.
                using (var request = PexelsClient.BuildApiRequest(url, apiKey))
                using (HttpResponseMessage response = await client.SendAsync(request, cancellationToken))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync(cancellationToken);
                        downloadUrl = PickBestPhoto(body, display);
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        EngineLog.Write($"pexels rate-limited for monitor '{display.DeviceName}' - falling back to Openverse");
                    }
                }

                // Fall back to Openverse when Pexels didn't deliver.
                if (downloadUrl is null)
                {
                    var items = await OpenverseClient.SearchAsync(client, tag, aspectRatio: "wide", pageSize: 25, cancellationToken);
                    downloadUrl = PickBestOpenversePhoto(items, display);
                    if (downloadUrl is not null) EngineLog.Write($"monitor '{display.DeviceName}' <- Openverse fallback");
                }

                if (downloadUrl is null) return;

                string savedPath = await DownloadToLibraryAsync(client, downloadUrl, libraryPath, cancellationToken);
                lock (lockObj)
                {
                    result[display.DeviceName] = savedPath;
                }
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                // Single-monitor failure should not kill the whole batch.
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return result;
    }

    /// <summary>Ultrawide panels want 'landscape' but with wide hints; portrait stacks go 'portrait'.</summary>
    public static string ClassifyOrientation(DisplayInfo display)
    {
        if (display.Height > display.Width) return "portrait";
        double ratio = display.Width / (double)Math.Max(1, display.Height);
        return ratio >= 2.0 ? "landscape" : "landscape";
    }

    private static string BuildSearchUrl(string tag, string orientation, int width, int height)
    {
        string encoded = Uri.EscapeDataString(tag);
        // Pull a page big enough to filter by min resolution client-side.
        return $"https://api.pexels.com/v1/search?query={encoded}&per_page=30&orientation={orientation}";
    }

    private static string? PickBestOpenversePhoto(IReadOnlyList<OpenverseClient.Candidate> items, DisplayInfo display)
    {
        double targetRatio = display.Width / (double)Math.Max(1, display.Height);
        (string? url, double score) best = (null, double.MaxValue);
        foreach (var item in items)
        {
            if (item.Width > 0 && item.Height > 0)
            {
                if (item.Width < display.Width || item.Height < display.Height) continue;
                double r = item.Width / (double)item.Height;
                double score = Math.Abs(r - targetRatio);
                if (score < best.score) best = (item.DownloadUrl, score);
            }
            else
            {
                // Unknown dimensions from Openverse - take it as a last resort.
                if (best.url is null) best = (item.DownloadUrl, double.MaxValue - 1);
            }
        }
        return best.url;
    }

    private static string? PickBestPhoto(string jsonBody, DisplayInfo display)
    {
        using var doc = JsonDocument.Parse(jsonBody);
        if (!doc.RootElement.TryGetProperty("photos", out JsonElement photos)
            || photos.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        double targetRatio = display.Width / (double)Math.Max(1, display.Height);
        (string? url, double score) best = (null, double.MaxValue);

        foreach (var photo in photos.EnumerateArray())
        {
            int w = photo.TryGetProperty("width", out var wEl) ? wEl.GetInt32() : 0;
            int h = photo.TryGetProperty("height", out var hEl) ? hEl.GetInt32() : 0;
            if (w < display.Width || h < display.Height)
            {
                continue;
            }

            if (!photo.TryGetProperty("src", out var src)) continue;
            string original = src.TryGetProperty("original", out var o) ? (o.GetString() ?? "") : "";
            if (string.IsNullOrWhiteSpace(original)) continue;

            double photoRatio = w / (double)Math.Max(1, h);
            double score = Math.Abs(photoRatio - targetRatio);
            if (score < best.score)
            {
                best = (original, score);
            }
        }

        return best.url;
    }

    private static async Task<string> DownloadToLibraryAsync(
        HttpClient client, string url, string libraryPath, CancellationToken cancellationToken)
    {
        string ext = PexelsClient.DetectExtension(url);
        string name = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";
        string target = Path.Combine(libraryPath, name);
        return await PexelsClient.DownloadToFileAsync(client, url, target, cancellationToken);
    }
}

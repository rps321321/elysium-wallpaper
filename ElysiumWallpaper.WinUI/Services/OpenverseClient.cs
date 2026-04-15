using System.Text.Json;

namespace ElysiumWallpaper.Services;

/// <summary>
/// Thin client for the public Openverse image-search API (no auth required for anonymous use).
/// Used as a fallback whenever Pexels is rate-limited or returns nothing usable - Openverse
/// aggregates CC0/CC-BY/CC-BY-SA content from Wikimedia, Flickr, museums, etc., filtered here
/// to commercially-usable licenses only.
/// </summary>
internal static class OpenverseClient
{
    private const string BaseUrl = "https://api.openverse.engineering/v1/images/";

    /// <summary>A minimal candidate shape compatible with the Pexels path's downstream scoring.</summary>
    public sealed record Candidate(string DownloadUrl, string ThumbUrl, int Width, int Height);

    /// <summary>
    /// Queries Openverse. Returns an empty list on any network / parse failure so callers can
    /// fall back to yet another source.
    /// </summary>
    public static async Task<IReadOnlyList<Candidate>> SearchAsync(
        HttpClient client,
        string query,
        string aspectRatio, // "wide" | "tall" | "square" | ""
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var parts = new List<string>
            {
                $"q={Uri.EscapeDataString(query)}",
                "license_type=commercial,modification",
                $"page_size={Math.Clamp(pageSize, 1, 40)}"
            };
            if (!string.IsNullOrWhiteSpace(aspectRatio)) parts.Add($"aspect_ratio={aspectRatio}");

            string url = $"{BaseUrl}?{string.Join("&", parts)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("ElysiumWallpaper/1.0");

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                EngineLog.Write($"openverse non-2xx: {(int)response.StatusCode} {response.ReasonPhrase}");
                return Array.Empty<Candidate>();
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<Candidate>();
            }

            var list = new List<Candidate>();
            foreach (var r in results.EnumerateArray())
            {
                string? dl = r.TryGetProperty("url", out var u) ? u.GetString() : null;
                string? thumb = r.TryGetProperty("thumbnail", out var t) ? t.GetString() : dl;
                int w = r.TryGetProperty("width", out var we) && we.ValueKind == JsonValueKind.Number ? we.GetInt32() : 0;
                int h = r.TryGetProperty("height", out var he) && he.ValueKind == JsonValueKind.Number ? he.GetInt32() : 0;
                if (!string.IsNullOrWhiteSpace(dl) && !string.IsNullOrWhiteSpace(thumb))
                {
                    list.Add(new Candidate(dl!, thumb!, w, h));
                }
            }
            return list;
        }
        catch (Exception ex)
        {
            EngineLog.Write($"openverse search '{query}' failed: {ex.Message}");
            return Array.Empty<Candidate>();
        }
    }
}

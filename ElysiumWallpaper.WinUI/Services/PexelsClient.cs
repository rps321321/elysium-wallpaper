namespace ElysiumWallpaper.Services;

/// <summary>
/// Thin helpers shared by <see cref="SlotAwareFetchService"/> and <see cref="MonitorFetchService"/>:
/// authenticated API calls, extension detection, and streaming downloads to a target path.
/// </summary>
internal static class PexelsClient
{
    public static readonly string[] KnownExtensions = [".jpg", ".jpeg", ".png", ".bmp"];

    public static HttpRequestMessage BuildApiRequest(string url, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", apiKey);
        request.Headers.UserAgent.ParseAdd("ElysiumWallpaper/1.0");
        return request;
    }

    public static async Task<string> DownloadToFileAsync(
        HttpClient client,
        string url,
        string targetPath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(targetPath);
        await input.CopyToAsync(output, cancellationToken);
        return targetPath;
    }

    /// <summary>Returns the file extension (including the dot) for a Pexels image URL, defaulting to .jpg.</summary>
    public static string DetectExtension(string url)
    {
        string ext = Path.GetExtension(new Uri(url).AbsolutePath.ToLowerInvariant());
        return KnownExtensions.Contains(ext) ? ext : ".jpg";
    }
}

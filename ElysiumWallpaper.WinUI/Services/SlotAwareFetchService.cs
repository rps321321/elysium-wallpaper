using System.Net.Http.Headers;
using System.Text.Json;

namespace ElysiumWallpaper.Services;

/// <summary>
/// Fetches one Pexels image per time-of-day slot using queries that actually match the mood of
/// that slot - no more "sunset at morning, neon city at noon" surprises. Writes images/morning.jpg
/// etc. and a fresh slot_manifest.txt. Replaces the fragile scraping-based fetch_pexels.py.
/// </summary>
public static class SlotAwareFetchService
{
    /// <summary>
    /// Per-slot fetch config.
    /// <list type="bullet">
    /// <item><c>queries</c>: large pool of specific, unambiguous search terms for the slot.</item>
    /// <item><c>color</c>: Pexels color hint for server-side bias.</item>
    /// <item><c>lumaMin/lumaMax</c>: acceptable mean-luminance band (0..1) for the final thumbnail check.</item>
    /// </list>
    /// Luminance check rejects a sunset that Pexels tagged as "sunrise", an over-bright night shot, etc.
    /// </summary>
    private static readonly Dictionary<string, SlotFetchConfig> SlotConfig = new()
    {
        ["morning"] = new(
            queries:
            [
                "sunrise mountain", "dawn sky", "foggy morning forest", "morning mist valley",
                "sunrise ocean", "sunrise field", "early morning light", "pastel sunrise",
                "soft morning landscape", "dawn meadow", "misty sunrise", "sunrise silhouette"
            ],
            color: "yellow",
            lumaMin: 0.35, lumaMax: 0.65,
            // Morning should have warm cast AND not be pure-dark night: avg R >= avg B, not too dark.
            extraGate: c => c.redMean >= c.blueMean && c.meanLuma > 0.25),

        ["noon"] = new(
            queries:
            [
                "bright blue sky", "sunny mountain", "midday beach", "summer field",
                "clear sky landscape", "daylight forest", "azure sky", "bright ocean",
                "sunny meadow", "midday desert", "vivid landscape", "sunlit valley"
            ],
            color: "blue",
            lumaMin: 0.60, lumaMax: 0.95,
            extraGate: c => c.brightPixelRatio > 0.30),

        ["evening"] = new(
            queries:
            [
                "sunset ocean", "sunset mountain", "golden hour forest", "dusk sky",
                "evening silhouette", "warm sunset", "sunset desert", "orange sunset",
                "amber sunset", "sunset reflection", "sunset cloud", "twilight landscape"
            ],
            color: "orange",
            lumaMin: 0.20, lumaMax: 0.55,
            // Warm: red dominates blue by a real margin, not just a tie.
            extraGate: c => c.redMean > c.blueMean + 15),

        ["night"] = new(
            queries:
            [
                "starry night sky", "milky way", "aurora borealis", "moonlight forest",
                "night city skyline", "dark night ocean", "night mountain",
                "northern lights", "night desert stars", "moonlit landscape",
                "midnight forest", "dark night sky", "night stars", "starry mountains"
            ],
            color: "black",
            lumaMin: 0.00, lumaMax: 0.20,
            // At least 60% of pixels should be dark, and almost no "sky-blue-bright" pixels.
            extraGate: c => c.darkPixelRatio > 0.60 && c.brightPixelRatio < 0.05)
    };

    private sealed record Candidate(double meanLuma, double darkPixelRatio, double brightPixelRatio, double redMean, double greenMean, double blueMean);
    private sealed record SlotFetchConfig(
        string[] queries,
        string color,
        double lumaMin,
        double lumaMax,
        Func<Candidate, bool> extraGate);

    private static readonly string[] KnownExtensions = PexelsClient.KnownExtensions;

    /// <summary>
    /// Fetches one slot-appropriate image per slot and writes them to <paramref name="imagesDir"/>.
    /// Returns true when every slot got an image, false if any fell back to an existing file.
    /// </summary>
    public static async Task<bool> FetchAllSlotsAsync(
        HttpClient client,
        string? apiKey,
        string imagesDir,
        int minWidth,
        int minHeight,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(imagesDir);
        var mapping = new Dictionary<string, string>();
        bool allOk = true;

        foreach (var (slot, cfg) in SlotConfig)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? savedFile = null;
            try
            {
                savedFile = await FetchOneSlotAsync(client, apiKey, slot, cfg, imagesDir, minWidth, minHeight, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                EngineLog.Write($"slot fetch '{slot}' failed: {ex.Message}");
            }

            if (savedFile is null)
            {
                savedFile = FindExistingSlotFile(imagesDir, slot);
                allOk = false;
            }
            if (savedFile is not null)
            {
                mapping[slot] = savedFile;
            }
        }

        WriteManifest(imagesDir, mapping);
        return allOk;
    }

    /// <summary>
    /// Public single-slot variant. Used when the user dislikes the current slot's image and wants
    /// an alternative - refetches only the specified slot and updates the manifest line for it.
    /// </summary>
    public static async Task<string?> FetchSingleSlotAsync(
        HttpClient client,
        string? apiKey,
        string slot,
        string imagesDir,
        int minWidth,
        int minHeight,
        CancellationToken cancellationToken)
    {
        if (!SlotConfig.TryGetValue(slot, out var cfg)) return null;
        Directory.CreateDirectory(imagesDir);

        string? saved = await FetchOneSlotAsync(client, apiKey, slot, cfg, imagesDir, minWidth, minHeight, cancellationToken);
        if (saved is not null)
        {
            UpdateManifestLine(imagesDir, slot, saved);
        }
        return saved;
    }

    /// <summary>Rewrites just one slot line of the manifest, preserving the others.</summary>
    private static void UpdateManifestLine(string imagesDir, string slot, string filename)
    {
        var manifestPath = Path.Combine(imagesDir, SlotManifest.FileName);
        var mapping = File.Exists(manifestPath)
            ? new Dictionary<string, string>(SlotManifest.Parse(File.ReadAllLines(manifestPath)), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        mapping[slot] = filename;
        WriteManifest(imagesDir, mapping);
    }

    private static async Task<string?> FetchOneSlotAsync(
        HttpClient client, string? apiKey, string slot, SlotFetchConfig cfg,
        string imagesDir, int minWidth, int minHeight, CancellationToken cancellationToken)
    {
        double targetLuma = (cfg.lumaMin + cfg.lumaMax) / 2.0;
        var allScored = new List<(string url, int w, int h, Candidate c, string query, string source)>();
        // Skip Pexels entirely when the user hasn't supplied a key — Openverse handles the rest.
        bool pexelsExhausted = string.IsNullOrWhiteSpace(apiKey);

        // --- Round 1: Pexels (auth required, curated quality) ---
        foreach (var query in cfg.queries.OrderBy(_ => Random.Shared.Next()).Take(4))
        {
            if (pexelsExhausted) break;
            cancellationToken.ThrowIfCancellationRequested();
            string url = $"https://api.pexels.com/v1/search?query={Uri.EscapeDataString(query)}&per_page=30&orientation=landscape&color={cfg.color}";

            // apiKey non-null here: pexelsExhausted is set to true when apiKey is null/empty,
            // so we'd have broken out of the loop above.
            using var request = PexelsClient.BuildApiRequest(url, apiKey!);
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                EngineLog.Write($"pexels rate-limited for '{slot}' / '{query}' - will try Openverse");
                pexelsExhausted = true;
                break;
            }
            if (!response.IsSuccessStatusCode) continue;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!doc.RootElement.TryGetProperty("photos", out var photos) || photos.ValueKind != JsonValueKind.Array) continue;

            int examined = 0;
            foreach (var p in photos.EnumerateArray())
            {
                if (examined >= 8) break;
                int w = p.TryGetProperty("width", out var we) ? we.GetInt32() : 0;
                int h = p.TryGetProperty("height", out var he) ? he.GetInt32() : 0;
                if (w < minWidth || h < minHeight) continue;
                if (!p.TryGetProperty("src", out var src)) continue;

                string? originalUrl = src.TryGetProperty("original", out var o) ? o.GetString() : null;
                string? analysisUrl = src.TryGetProperty("small", out var sm) ? sm.GetString() :
                                       src.TryGetProperty("medium", out var md) ? md.GetString() :
                                       src.TryGetProperty("tiny", out var tn) ? tn.GetString() : originalUrl;
                if (string.IsNullOrWhiteSpace(originalUrl) || string.IsNullOrWhiteSpace(analysisUrl)) continue;

                Candidate? analysis = await AnalyzeImageAsync(client, analysisUrl, cancellationToken);
                if (analysis is null) continue;
                examined++;

                allScored.Add((originalUrl, w, h, analysis, query, "pexels"));

                if (IsGatedPass(analysis, cfg))
                {
                    return await SaveAndLogAsync(client, slot, imagesDir, originalUrl, w, h, query, analysis, cfg, "pexels", cancellationToken);
                }
            }
        }

        // --- Round 2: Openverse fallback ---
        // Always run when Pexels was rate-limited; also run when Pexels yielded no gate-passing
        // candidates so we widen the candidate pool before falling back to best-scored.
        bool needFallback = pexelsExhausted || allScored.All(s => !IsGatedPass(s.c, cfg));
        if (needFallback)
        {
            foreach (var query in cfg.queries.OrderBy(_ => Random.Shared.Next()).Take(3))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var items = await OpenverseClient.SearchAsync(client, query, aspectRatio: "wide", pageSize: 20, cancellationToken);
                int examined = 0;
                foreach (var item in items)
                {
                    if (examined >= 8) break;
                    if (item.Width > 0 && item.Height > 0 && (item.Width < minWidth || item.Height < minHeight)) continue;

                    Candidate? analysis = await AnalyzeImageAsync(client, item.ThumbUrl, cancellationToken);
                    if (analysis is null) continue;
                    examined++;

                    int w = item.Width > 0 ? item.Width : minWidth;
                    int h = item.Height > 0 ? item.Height : minHeight;
                    allScored.Add((item.DownloadUrl, w, h, analysis, query, "openverse"));

                    if (IsGatedPass(analysis, cfg))
                    {
                        return await SaveAndLogAsync(client, slot, imagesDir, item.DownloadUrl, w, h, query, analysis, cfg, "openverse", cancellationToken);
                    }
                }
            }
        }

        if (allScored.Count == 0) return null;

        var best = allScored
            .OrderBy(x => Math.Abs(x.c.meanLuma - targetLuma) + HueMismatchPenalty(x.c, slot))
            .First();
        string fallbackName = await DownloadToSlotAsync(client, best.url, slot, imagesDir, cancellationToken);
        EngineLog.Write($"slot '{slot}' FALLBACK ({best.source}) <- '{best.query}' luma={best.c.meanLuma:F2} dark%={best.c.darkPixelRatio:F2} (target {cfg.lumaMin:F2}-{cfg.lumaMax:F2}) {best.w}x{best.h}");
        return fallbackName;
    }

    /// <summary>Does this candidate land in the slot's luma band AND pass the extra mood gate?</summary>
    private static bool IsGatedPass(Candidate c, SlotFetchConfig cfg)
        => c.meanLuma >= cfg.lumaMin && c.meanLuma <= cfg.lumaMax && cfg.extraGate(c);

    /// <summary>Downloads the chosen image + writes the engine-log line that records the pick.</summary>
    private static async Task<string> SaveAndLogAsync(
        HttpClient client, string slot, string imagesDir,
        string originalUrl, int w, int h, string query, Candidate analysis,
        SlotFetchConfig cfg, string source, CancellationToken cancellationToken)
    {
        string saved = await DownloadToSlotAsync(client, originalUrl, slot, imagesDir, cancellationToken);
        EngineLog.Write($"slot '{slot}' ({source}) <- '{query}' PASS luma={analysis.meanLuma:F2} dark%={analysis.darkPixelRatio:F2} bright%={analysis.brightPixelRatio:F2} R{analysis.redMean:F0}/G{analysis.greenMean:F0}/B{analysis.blueMean:F0} {w}x{h}");
        return saved;
    }

    private static double HueMismatchPenalty(Candidate c, string slot) => slot switch
    {
        "morning" or "evening" => Math.Max(0, c.blueMean - c.redMean) / 255.0,  // penalize blue-dominant warm slots
        "noon" => Math.Max(0, c.redMean - c.blueMean) / 255.0,
        "night" => c.brightPixelRatio * 2.0,  // any brightness in night is a big penalty
        _ => 0
    };

    /// <summary>
    /// Downloads a scaled-down preview and computes mean luma, dark/bright pixel ratios, and
    /// per-channel means. Far richer than a single average so slot gates can enforce mood.
    /// </summary>
    private static async Task<Candidate?> AnalyzeImageAsync(HttpClient client, string previewUrl, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await client.GetByteArrayAsync(previewUrl, cancellationToken);
            if (bytes.Length < 100) return null;

            using var ms = new MemoryStream(bytes);
            var ras = ms.AsRandomAccessStream();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);

            var pixelData = await decoder.GetPixelDataAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
                new Windows.Graphics.Imaging.BitmapTransform(),
                Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);

            byte[] px = pixelData.DetachPixelData();
            long sumR = 0, sumG = 0, sumB = 0, sumLuma = 0;
            int dark = 0, bright = 0, total = 0;

            for (int i = 0; i < px.Length; i += 4 * 8)
            {
                byte b = px[i];
                byte g = px[i + 1];
                byte r = px[i + 2];
                int y = (int)(0.299 * r + 0.587 * g + 0.114 * b);

                sumR += r; sumG += g; sumB += b; sumLuma += y;
                if (y < 60) dark++;       // ~0.23
                else if (y > 200) bright++; // ~0.78
                total++;
            }
            if (total == 0) return null;
            return new Candidate(
                meanLuma: (sumLuma / (double)total) / 255.0,
                darkPixelRatio: dark / (double)total,
                brightPixelRatio: bright / (double)total,
                redMean: sumR / (double)total,
                greenMean: sumG / (double)total,
                blueMean: sumB / (double)total);
        }
        catch (OperationCanceledException)
        {
            // Propagate so the outer fetch loop stops scoring more candidates instead
            // of burning network on dozens of thumbnails after the user cancelled.
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> DownloadToSlotAsync(
        HttpClient client, string url, string slot, string imagesDir, CancellationToken cancellationToken)
    {
        // Probe extension via a HEAD-ish request (actually full GET: needed anyway for body).
        // Unique epoch+random-suffixed filename so every reroll produces a NEW path - Windows'
        // SPI cache matches on the old path and skips the refresh if we overwrite `night.jpg`
        // in place. Pure-millisecond suffixes collided when two reroll clicks landed in the
        // same ms; the 8-char Guid fragment makes that effectively impossible.
        string ext = PexelsClient.DetectExtension(url);
        string fileName = $"{slot}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid().ToString("N")[..8]}{ext}";
        string targetPath = Path.Combine(imagesDir, fileName);

        await PexelsClient.DownloadToFileAsync(client, url, targetPath, cancellationToken);
        RemoveExistingSlotFiles(imagesDir, slot, except: fileName);
        return fileName;
    }

    /// <summary>
    /// Removes any file that matches <c>{slot}.*</c> or <c>{slot}-*.*</c> in the images folder,
    /// except the one we just wrote. Keeps the folder tidy while guaranteeing unique filenames.
    /// </summary>
    private static void RemoveExistingSlotFiles(string imagesDir, string slot, string? except = null)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(imagesDir))
            {
                string name = Path.GetFileName(path);
                if (except is not null && string.Equals(name, except, StringComparison.OrdinalIgnoreCase)) continue;

                string baseName = Path.GetFileNameWithoutExtension(name);
                // Matches "night" or "night-123456789"
                if (string.Equals(baseName, slot, StringComparison.OrdinalIgnoreCase)
                    || baseName.StartsWith(slot + "-", StringComparison.OrdinalIgnoreCase))
                {
                    if (KnownExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
                    {
                        try { File.Delete(path); } catch (Exception ex) { EngineLog.Write($"slot-file cleanup failed for {path}: {ex.Message}"); }
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static string? FindExistingSlotFile(string imagesDir, string slot)
    {
        foreach (var ext in KnownExtensions)
        {
            var name = slot + ext;
            if (File.Exists(Path.Combine(imagesDir, name))) return name;
        }
        return null;
    }

    private static void WriteManifest(string imagesDir, IDictionary<string, string> mapping)
    {
        var path = Path.Combine(imagesDir, SlotManifest.FileName);
        var lines = new[] { "morning", "noon", "evening", "night" }
            .Select(s => $"{s}={(mapping.TryGetValue(s, out var f) ? f : "")}");
        File.WriteAllLines(path, lines);
    }
}

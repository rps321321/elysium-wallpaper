using System.Reflection;
using System.Text.Json;

namespace ElysiumWallpaper.Services;

/// <summary>
/// Polls the GitHub Releases API for a newer version than the running build. Pure helper —
/// the UI layer (MainPage) decides what to do with a positive result.
///
/// Assembly version is the source of truth for "current" — bump <c>&lt;Version&gt;</c> in the
/// csproj before tagging a release. Tag convention: <c>v1.2.3</c> (the leading <c>v</c> is
/// stripped before comparison so <c>1.2.3</c> tags work too).
///
/// Best-effort: every failure path returns <see cref="UpdateInfo.None"/>. The user never sees
/// a "couldn't reach GitHub" error — silent on the happy unreachable case, loud only when an
/// update is actually available.
/// </summary>
public static class UpdateCheckService
{
    private const string Owner = "rps321321";
    private const string Repo  = "elysium-wallpaper";
    private const string LatestReleaseEndpoint =
        $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    // Static so we share the connection pool and don't leak handlers across calls.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Result of a check. <see cref="HasUpdate"/> is false when up-to-date or on error.</summary>
    public readonly record struct UpdateInfo(bool HasUpdate, Version? Latest, string? ReleaseUrl)
    {
        public static UpdateInfo None => new(false, null, null);
    }

    public static async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var current = Assembly.GetExecutingAssembly().GetName().Version;
            if (current is null) return UpdateInfo.None;

            using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
            // GitHub requires User-Agent on all API calls.
            req.Headers.UserAgent.ParseAdd($"{Repo}/{current.ToString(3)}");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await Http.SendAsync(req, ct);
            // 404 means "no releases yet" — completely normal early in a project's life.
            if (!resp.IsSuccessStatusCode) return UpdateInfo.None;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            string? url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return UpdateInfo.None;

            // Strip leading 'v' / 'V' from tag if present (GitHub convention).
            string normalized = tag.TrimStart('v', 'V');
            if (!Version.TryParse(normalized, out var latest)) return UpdateInfo.None;

            // Treat as newer iff the *meaningful* part of the version (Major.Minor.Build)
            // is greater. Revision is the auto-bumped 4th part we don't care about.
            int cmp = NormalizedCompare(latest, current);
            return cmp > 0
                ? new UpdateInfo(true, latest, url)
                : UpdateInfo.None;
        }
        catch (Exception ex)
        {
            EngineLog.Write($"update-check failed: {ex.Message}");
            return UpdateInfo.None;
        }
    }

    /// <summary>Compares Major.Minor.Build only (Revision is ignored — it's auto-bumped noise).</summary>
    private static int NormalizedCompare(Version a, Version b)
    {
        int aMaj = a.Major, aMin = Math.Max(0, a.Minor), aBld = Math.Max(0, a.Build);
        int bMaj = b.Major, bMin = Math.Max(0, b.Minor), bBld = Math.Max(0, b.Build);
        if (aMaj != bMaj) return aMaj.CompareTo(bMaj);
        if (aMin != bMin) return aMin.CompareTo(bMin);
        return aBld.CompareTo(bBld);
    }
}

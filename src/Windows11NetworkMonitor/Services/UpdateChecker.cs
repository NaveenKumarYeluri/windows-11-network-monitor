using System.Reflection;
using System.Text.Json;

namespace Windows11NetworkMonitor.Services;

/// <summary>
/// Checks GitHub Releases for a newer version than the running assembly.
/// Unauthenticated single request (startup + manual from the tray menu);
/// no data about the user is sent - the only headers are a User-Agent
/// (required by the API) and nothing else.
/// </summary>
public sealed class UpdateChecker
{
    public const string RepoOwner = "NaveenKumarYeluri";
    public const string RepoName = "windows-11-network-monitor";

    public static string ReleasesUrl => $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

    /// <summary>Tag + URL of a newer release, or null when up-to-date.</summary>
    public sealed record UpdateInfo(string Tag, string Url);

    /// <summary>
    /// Null = up to date (or no network / rate limited) - the caller decides
    /// whether to surface anything.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
#if DEBUG
        // Don't phone home or nag while testing local builds.
        await Task.CompletedTask;
        return null;
#else
        try
        {
            var current = CurrentVersion();
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Windows11NetworkMonitor/{current.ToString(3)}");

            using var response = await http.GetAsync(
                $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Drafts and prereleases are not returned by this endpoint; only stable releases.
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return null;

            if (ParseTag(tag) is not { } remote || remote <= current) return null;

            var url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString() : null;
            return new UpdateInfo(tag, string.IsNullOrWhiteSpace(url) ? ReleasesUrl : url);
        }
        catch
        {
            return null;
        }
#endif
    }

    /// <summary>Accepts "v1.0.2", "V1.0.2", "1.0.2", "1.0.2-beta" → 1.0.2.0.</summary>
    private static Version? ParseTag(string tag)
    {
        var s = tag.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        var dash = s.IndexOf('-');
        if (dash > 0) s = s[..dash];
        return Version.TryParse(s, out var v) ? v : null;
    }

    /// <summary>Version of the running exe (1.0.1.0 etc. from csproj).</summary>
    public static Version CurrentVersion()
        => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);
}

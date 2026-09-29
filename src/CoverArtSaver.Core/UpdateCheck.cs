using System.Net.Http.Headers;
using System.Text.Json;

namespace CoverArtSaver.Core;

/// <summary>A newer release on GitHub. <see cref="InstallerUrl"/> is null for releases from before there was an installer.</summary>
public sealed record AvailableUpdate(Version Version, string ReleaseUrl, string? InstallerUrl, string? InstallerName);

/// <summary>
/// Asks GitHub for the latest release. Only the settings window does this (never the screensaver itself), and only
/// while "Check for updates" is on. Nothing is sent except the request itself.
/// </summary>
public static class UpdateCheck
{
    public const string LatestReleaseApi = "https://api.github.com/repos/ShawnPlays/pc-game-cover-art-screensaver/releases/latest";

    /// <returns>The newer release, or null if this is the latest.</returns>
    /// <exception cref="HttpRequestException">No connection, or GitHub said no.</exception>
    public static async Task<AvailableUpdate?> CheckAsync(Version current, HttpClient http, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PCGameCoverArt", Normalize(current).ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancel), current);
    }

    /// <summary>Reads GitHub's "latest release" JSON.</summary>
    public static AvailableUpdate? Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;
        var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || Normalize(latest) <= Normalize(current))
        {
            return null;
        }

        var releaseUrl = release.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        string? installerUrl = null, installerName = null;
        if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name != null && name.StartsWith("PCGameCoverArtSetup", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    && asset.TryGetProperty("browser_download_url", out var url))
                {
                    installerUrl = url.GetString();
                    installerName = name;
                    break;
                }
            }
        }

        return new AvailableUpdate(Normalize(latest), releaseUrl, installerUrl, installerName);
    }

    /// <summary>1.3.2.0 and 1.3.2 are the same version.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}

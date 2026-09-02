using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClassRoom_Control.Services.Common;

public class UpdateInfo
{
    public bool HasUpdate { get; set; }
    public string NewVersion { get; set; } = string.Empty;
    public string Changelog { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string ReleasePageUrl { get; set; } = string.Empty;
}

public static class UpdateService
{
    private static readonly HttpClient _client = new();

    static UpdateService()
    {
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("ClassRoom-Control-App");
    }

    public static async Task<UpdateInfo> CheckForUpdatesAsync(string? owner = null, string? repo = null)
    {
        owner ??= AppMetadata.GitHubOwner;
        repo ??= AppMetadata.GitHubRepo;

        var info = new UpdateInfo();

        try
        {
            string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            var response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return info;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            string tagName = doc.RootElement.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(tagName)) return info;

            if (IsNewerVersion(tagName, AppMetadata.Version))
            {
                info.HasUpdate = true;
                info.NewVersion = tagName;
                info.Changelog = doc.RootElement.TryGetProperty("body", out var bProp) ? bProp.GetString() ?? string.Empty : string.Empty;
                info.ReleasePageUrl = doc.RootElement.TryGetProperty("html_url", out var hProp) ? hProp.GetString() ?? string.Empty : string.Empty;

                // Search for .exe installer or .zip in assets
                if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string assetName = asset.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? string.Empty : string.Empty;
                        if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            info.DownloadUrl = asset.TryGetProperty("browser_download_url", out var dlProp) ? dlProp.GetString() ?? string.Empty : string.Empty;
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
        }

        return info;
    }

    private static bool IsNewerVersion(string remoteTag, string currentTag)
    {
        string cleanRemote = NormalizeVersion(remoteTag);
        string cleanCurrent = NormalizeVersion(currentTag);

        if (Version.TryParse(cleanRemote, out var vRemote) && Version.TryParse(cleanCurrent, out var vCurrent))
        {
            return vRemote > vCurrent;
        }

        return !string.Equals(remoteTag.Trim(), currentTag.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string raw)
    {
        var sb = new StringBuilder();
        foreach (char c in raw)
        {
            if (char.IsDigit(c) || c == '.') sb.Append(c);
        }
        string s = sb.ToString().Trim('.');
        if (string.IsNullOrEmpty(s)) return "0.0.0";
        var parts = s.Split('.');
        if (parts.Length == 1) return $"{parts[0]}.0.0";
        if (parts.Length == 2) return $"{parts[0]}.{parts[1]}.0";
        return s;
    }
}

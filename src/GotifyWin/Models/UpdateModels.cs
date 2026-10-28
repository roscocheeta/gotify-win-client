using System.Text.Json.Serialization;

namespace GotifyWin.Models;

public class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAssetDto> Assets { get; set; } = new();
}

public class GitHubReleaseAssetDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;
}

public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; init; }
    public Version CurrentVersion { get; init; } = new(0, 0, 0);
    public Version? LatestVersion { get; init; }
    public string LatestVersionTag { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string ReleasePageUrl { get; init; } = string.Empty;
    public DateTime? PublishedAt { get; init; }
    public string? DownloadUrl { get; init; }
    public string? AssetFileName { get; init; }
    public string? ChecksumsUrl { get; init; }
    public long? AssetSize { get; init; }
    public string? ErrorMessage { get; init; }
    public GitHubReleaseDto? ReleaseInfo { get; init; }
}

public enum UpdateStatus
{
    Idle,
    Checking,
    UpdateAvailable,
    UpToDate,
    Downloading,
    ReadyToInstall,
    Applying,
    Failed
}

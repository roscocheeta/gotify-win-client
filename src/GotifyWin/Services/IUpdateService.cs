using GotifyWin.Models;

namespace GotifyWin.Services;

public interface IUpdateService
{
    Version CurrentVersion { get; }
    string CurrentVersionString { get; }

    event Action<UpdateCheckResult>? UpdateAvailable;

    Task<UpdateCheckResult> CheckForUpdatesAsync(bool isManual = false, CancellationToken cancellationToken = default);
    Task<string> DownloadUpdateAsync(GitHubReleaseDto release, GitHubReleaseAssetDto asset, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> VerifyChecksumAsync(string zipFilePath, string expectedChecksum, CancellationToken cancellationToken = default);
    Task<string?> FetchChecksumForAssetAsync(GitHubReleaseDto release, string assetName, CancellationToken cancellationToken = default);
    Task<bool> ApplyUpdateAndRestartAsync(string zipFilePath);
    void CleanUpOldExecutables();
}

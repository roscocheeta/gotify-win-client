using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using GotifyWin.Models;

namespace GotifyWin.Services;

public class UpdateService : IUpdateService, IDisposable
{
    private const string DefaultRepoOwner = "roscocheeta";
    private const string DefaultRepoName = "gotify-win-client";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _repoOwner;
    private readonly string _repoName;

    public Version CurrentVersion { get; }
    public string CurrentVersionString => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public event Action<UpdateCheckResult>? UpdateAvailable;

    public UpdateService(
        HttpClient? httpClient = null,
        string repoOwner = DefaultRepoOwner,
        string repoName = DefaultRepoName,
        Version? currentVersionOverride = null)
    {
        _repoOwner = repoOwner;
        _repoName = repoName;

        if (currentVersionOverride != null)
        {
            CurrentVersion = currentVersionOverride;
        }
        else
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
            var ver = assembly.GetName().Version;
            CurrentVersion = ver != null ? new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build)) : new Version(1, 1, 0);
        }

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("GotifyWin-Updater", CurrentVersionString));
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            _ownsHttpClient = true;
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool isManual = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestUri = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GotifyWin-Updater", CurrentVersionString));
            }
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorMsg = $"GitHub API returned status {(int)response.StatusCode} ({response.ReasonPhrase})";
                Debug.WriteLine($"[UpdateService] {errorMsg}");
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = CurrentVersion,
                    ErrorMessage = errorMsg
                };
            }

            var jsonStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var releaseDto = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(
                jsonStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            if (releaseDto == null || string.IsNullOrWhiteSpace(releaseDto.TagName))
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = CurrentVersion,
                    ErrorMessage = "Invalid release payload received from GitHub."
                };
            }

            if (!TryParseVersion(releaseDto.TagName, out var latestVersion))
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = CurrentVersion,
                    ErrorMessage = $"Could not parse remote version tag '{releaseDto.TagName}'."
                };
            }

            var isNewer = latestVersion > CurrentVersion;

            // Determine matching asset (standalone vs light)
            var chosenAsset = SelectBestAsset(releaseDto.Assets);
            var checksumsAsset = releaseDto.Assets.FirstOrDefault(a =>
                a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));

            var result = new UpdateCheckResult
            {
                IsUpdateAvailable = isNewer,
                CurrentVersion = CurrentVersion,
                LatestVersion = latestVersion,
                LatestVersionTag = releaseDto.TagName,
                ReleaseName = releaseDto.Name,
                ReleaseNotes = releaseDto.Body ?? string.Empty,
                ReleasePageUrl = releaseDto.HtmlUrl,
                PublishedAt = releaseDto.PublishedAt,
                DownloadUrl = chosenAsset?.BrowserDownloadUrl,
                AssetFileName = chosenAsset?.Name,
                AssetSize = chosenAsset?.Size,
                ChecksumsUrl = checksumsAsset?.BrowserDownloadUrl,
                ReleaseInfo = releaseDto
            };

            if (isNewer)
            {
                UpdateAvailable?.Invoke(result);
            }

            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or IOException)
        {
            Debug.WriteLine($"[UpdateService] CheckForUpdatesAsync failed: {ex.Message}");
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = CurrentVersion,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<string> DownloadUpdateAsync(
        GitHubReleaseDto release,
        GitHubReleaseAssetDto asset,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            throw new ArgumentException("Asset download URL cannot be empty.", nameof(asset));
        }

        var updateDir = Path.Combine(Path.GetTempPath(), "GotifyWin-Update");
        Directory.CreateDirectory(updateDir);

        var destinationFilePath = Path.Combine(updateDir, asset.Name);
        if (File.Exists(destinationFilePath))
        {
            try { File.Delete(destinationFilePath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                destinationFilePath = Path.Combine(updateDir, $"{Path.GetFileNameWithoutExtension(asset.Name)}_{DateTime.UtcNow.Ticks}.zip");
            }
        }

        using var response = await _httpClient.GetAsync(
            asset.BrowserDownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytesRead += bytesRead;

            if (totalBytes > 0 && progress != null)
            {
                var percentage = (double)totalBytesRead / totalBytes * 100.0;
                progress.Report(Math.Min(100.0, percentage));
            }
        }

        progress?.Report(100.0);
        return destinationFilePath;
    }

    public async Task<string?> FetchChecksumForAssetAsync(
        GitHubReleaseDto release,
        string assetName,
        CancellationToken cancellationToken = default)
    {
        var checksumAsset = release.Assets.FirstOrDefault(a =>
            a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));

        if (checksumAsset == null || string.IsNullOrWhiteSpace(checksumAsset.BrowserDownloadUrl))
        {
            return null;
        }

        try
        {
            var checksumText = await _httpClient.GetStringAsync(checksumAsset.BrowserDownloadUrl, cancellationToken);
            var checksums = ParseChecksums(checksumText);

            if (checksums.TryGetValue(assetName, out var hash))
            {
                return hash;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            Debug.WriteLine($"[UpdateService] FetchChecksumForAssetAsync failed: {ex.Message}");
        }

        return null;
    }

    public async Task<bool> VerifyChecksumAsync(
        string zipFilePath,
        string expectedChecksum,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipFilePath))
        {
            throw new FileNotFoundException("File not found for checksum verification.", zipFilePath);
        }

        if (string.IsNullOrWhiteSpace(expectedChecksum))
        {
            throw new ArgumentException("Expected checksum cannot be null or empty.", nameof(expectedChecksum));
        }

        await using var stream = File.OpenRead(zipFilePath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var computedHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return string.Equals(computedHex, expectedChecksum.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> ApplyUpdateAndRestartAsync(string zipFilePath)
    {
        if (!File.Exists(zipFilePath))
        {
            throw new FileNotFoundException("Update archive not found.", zipFilePath);
        }

        var currentProcessPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentProcessPath) || !File.Exists(currentProcessPath))
        {
            throw new InvalidOperationException("Could not determine current executable path.");
        }

        var appDir = Path.GetDirectoryName(currentProcessPath)!;
        var tempExtractDir = Path.Combine(Path.GetTempPath(), "GotifyWin-Update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempExtractDir);

        string backupPath = currentProcessPath + ".old";

        return await Task.Run(() =>
        {
            try
            {
                // 1. Extract zip to temp staging directory
                ZipFile.ExtractToDirectory(zipFilePath, tempExtractDir, overwriteFiles: true);

                // 2. Identify new executable in extracted files
                var newExePath = Path.Combine(tempExtractDir, "GotifyWin.exe");
                if (!File.Exists(newExePath))
                {
                    var found = Directory.GetFiles(tempExtractDir, "GotifyWin.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (found != null)
                    {
                        newExePath = found;
                    }
                    else
                    {
                        throw new FileNotFoundException("GotifyWin.exe was not found in the update archive.");
                    }
                }

                // 3. Rename current running executable to .old
                if (File.Exists(backupPath))
                {
                    try { File.Delete(backupPath); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        backupPath = currentProcessPath + $".{DateTime.UtcNow.Ticks}.old";
                    }
                }

                File.Move(currentProcessPath, backupPath);

                // 4. Copy new GotifyWin.exe into app directory
                File.Copy(newExePath, currentProcessPath, overwrite: true);

                // Copy any sibling files (e.g. pdb, dlls)
                var sourceDir = Path.GetDirectoryName(newExePath)!;
                foreach (var file in Directory.GetFiles(sourceDir))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.Equals("GotifyWin.exe", StringComparison.OrdinalIgnoreCase)) continue;

                    var dest = Path.Combine(appDir, fileName);
                    try
                    {
                        File.Copy(file, dest, overwrite: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Debug.WriteLine($"[UpdateService] Could not overwrite sibling file {fileName}: {ex.Message}");
                    }
                }

                // 5. Spawn updated executable
                var startInfo = new ProcessStartInfo
                {
                    FileName = currentProcessPath,
                    UseShellExecute = true,
                    WorkingDirectory = appDir
                };
                Process.Start(startInfo);

                // 6. Shutdown current app instance
                UiDispatcher.RunOnUI(() =>
                {
                    System.Windows.Application.Current?.Shutdown();
                });

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Debug.WriteLine($"[UpdateService] ApplyUpdateAndRestartAsync failed: {ex.Message}");

                // Rollback if needed
                if (!File.Exists(currentProcessPath) && File.Exists(backupPath))
                {
                    try
                    {
                        File.Move(backupPath, currentProcessPath);
                    }
                    catch (Exception rollbackEx) when (rollbackEx is IOException or UnauthorizedAccessException)
                    {
                        Debug.WriteLine($"[UpdateService] Rollback failed: {rollbackEx.Message}");
                    }
                }

                throw;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempExtractDir))
                    {
                        Directory.Delete(tempExtractDir, recursive: true);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine($"[UpdateService] Temp directory cleanup failed: {ex.Message}");
                }
            }
        });
    }

    public void CleanUpOldExecutables()
    {
        try
        {
            var currentProcessPath = Environment.ProcessPath;
            var baseDir = !string.IsNullOrEmpty(currentProcessPath)
                ? Path.GetDirectoryName(currentProcessPath)
                : AppDomain.CurrentDomain.BaseDirectory;

            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) return;

            var oldFiles = Directory.GetFiles(baseDir, "*.old");
            foreach (var file in oldFiles)
            {
                try
                {
                    File.Delete(file);
                    Debug.WriteLine($"[UpdateService] Removed old executable backup: {file}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine($"[UpdateService] Could not delete old file {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[UpdateService] CleanUpOldExecutables scan failed: {ex.Message}");
        }
    }

    public static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var clean = raw.Trim().TrimStart('v', 'V');
        var dashIndex = clean.IndexOf('-');
        if (dashIndex >= 0)
        {
            clean = clean[..dashIndex];
        }

        var parts = clean.Split('.');
        if (parts.Length == 1 && int.TryParse(parts[0], out var major))
        {
            version = new Version(major, 0, 0);
            return true;
        }

        if (parts.Length == 2 && int.TryParse(parts[0], out major) && int.TryParse(parts[1], out var minor))
        {
            version = new Version(major, minor, 0);
            return true;
        }

        if (Version.TryParse(clean, out var parsed) && parsed != null)
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    public static Dictionary<string, string> ParseChecksums(string checksumsText)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(checksumsText)) return dict;

        var lines = checksumsText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var hash = parts[0].Trim();
                var fileName = parts[1].Trim().TrimStart('*');
                dict[fileName] = hash;
            }
        }

        return dict;
    }

    private static GitHubReleaseAssetDto? SelectBestAsset(List<GitHubReleaseAssetDto> assets)
    {
        if (assets.Count == 0) return null;

        var currentProcessPath = Environment.ProcessPath;
        var isStandalone = true;
        if (!string.IsNullOrEmpty(currentProcessPath) && File.Exists(currentProcessPath))
        {
            try
            {
                var fileLen = new FileInfo(currentProcessPath).Length;
                isStandalone = fileLen > 20 * 1024 * 1024;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[UpdateService] Error determining executable size: {ex.Message}");
            }
        }

        if (isStandalone)
        {
            var standalone = assets.FirstOrDefault(a => a.Name.EndsWith("-standalone.zip", StringComparison.OrdinalIgnoreCase));
            if (standalone != null) return standalone;
        }
        else
        {
            var light = assets.FirstOrDefault(a => a.Name.EndsWith("-light.zip", StringComparison.OrdinalIgnoreCase));
            if (light != null) return light;
        }

        // Fallback to any win-x64 zip or first zip
        return assets.FirstOrDefault(a => a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using GotifyWin.ViewModels;
using Xunit;

namespace GotifyWin.Tests;

public class UpdateServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private class DummyPresenter : INotificationPresenter
    {
#pragma warning disable CS0067
        public event EventHandler<string>? UrlOpenRequested;
#pragma warning restore CS0067
        public void Present(GotifyMessage message, GotifyClientConfig config) { }
        public void OpenUrl(string url) { }
    }

    private class DummyConfigService : IConfigService
    {
        public GotifyClientConfig Config { get; set; } = new();
        public string ConfigFilePath => string.Empty;
        public bool HasDecryptionFailed => false;
#pragma warning disable CS0067
        public event EventHandler<GotifyClientConfig>? ConfigChanged;
#pragma warning restore CS0067
        public void Load() { }
        public void Save() { ConfigChanged?.Invoke(this, Config); }
    }

    private class DummyStartupManager : IStartupManager
    {
        public bool IsAutoStartEnabled() => false;
        public void SetAutoStart(bool enable) { }
    }

    [Theory]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("V1.2.0", 1, 2, 0)]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("v2.0.0-rc1", 2, 0, 0)]
    [InlineData("v1.5", 1, 5, 0)]
    [InlineData("2", 2, 0, 0)]
    public void TryParseVersion_ValidVersions_ParsesAccurately(string raw, int major, int minor, int build)
    {
        var success = UpdateService.TryParseVersion(raw, out var version);

        Assert.True(success);
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, Math.Max(0, version.Build));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    public void TryParseVersion_InvalidVersions_ReturnsFalse(string? raw)
    {
        var success = UpdateService.TryParseVersion(raw, out var version);

        Assert.False(success);
        Assert.Equal(0, version.Major);
    }

    [Fact]
    public void ParseChecksums_StandardChecksumsFile_ParsesCorrectly()
    {
        var checksumsText = @"
# SHA256 checksums
515771fc156a790ff35aa6583e1a76135e13a3e8a5d4f607e9628a5c83494890  GotifyWin-v1.1.0-win-x64-light.zip
b600654caa813227f154eb9191422c8276f122ce126deef3af235ce8923c00a5 *GotifyWin-v1.1.0-win-x64-standalone.zip
";

        var dict = UpdateService.ParseChecksums(checksumsText);

        Assert.Equal(2, dict.Count);
        Assert.Equal("515771fc156a790ff35aa6583e1a76135e13a3e8a5d4f607e9628a5c83494890", dict["GotifyWin-v1.1.0-win-x64-light.zip"]);
        Assert.Equal("b600654caa813227f154eb9191422c8276f122ce126deef3af235ce8923c00a5", dict["GotifyWin-v1.1.0-win-x64-standalone.zip"]);
        Assert.Equal("515771fc156a790ff35aa6583e1a76135e13a3e8a5d4f607e9628a5c83494890", dict["gotifywin-v1.1.0-win-x64-light.zip"]);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NewerVersionAvailable_ReportsUpdate()
    {
        var releaseJson = JsonSerializer.Serialize(new GitHubReleaseDto
        {
            TagName = "v1.2.0",
            Name = "v1.2.0 - Major Improvements",
            Body = "## Release Notes\n* Added updater",
            HtmlUrl = "https://github.com/roscocheeta/gotify-win-client/releases/tag/v1.2.0",
            PublishedAt = DateTime.UtcNow,
            Assets = new List<GitHubReleaseAssetDto>
            {
                new()
                {
                    Name = "GotifyWin-v1.2.0-win-x64-standalone.zip",
                    Size = 50000000,
                    BrowserDownloadUrl = "https://example.com/standalone.zip"
                },
                new()
                {
                    Name = "SHA256SUMS.txt",
                    Size = 120,
                    BrowserDownloadUrl = "https://example.com/SHA256SUMS.txt"
                }
            }
        });

        using var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
            });

        using var client = new HttpClient(handler);
        using var updateService = new UpdateService(client, currentVersionOverride: new Version(1, 1, 0));

        bool eventFired = false;
        updateService.UpdateAvailable += res =>
        {
            eventFired = true;
            Assert.Equal("v1.2.0", res.LatestVersionTag);
        };

        var result = await updateService.CheckForUpdatesAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.True(eventFired);
        Assert.Equal("v1.2.0", result.LatestVersionTag);
        Assert.Equal("v1.2.0 - Major Improvements", result.ReleaseName);
        Assert.Equal("https://example.com/standalone.zip", result.DownloadUrl);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_SameOrOlderVersion_ReportsNoUpdate()
    {
        var releaseJson = JsonSerializer.Serialize(new GitHubReleaseDto
        {
            TagName = "v1.1.0",
            Name = "v1.1.0 - Current",
            HtmlUrl = "https://github.com/roscocheeta/gotify-win-client/releases/tag/v1.1.0",
            Assets = new List<GitHubReleaseAssetDto>()
        });

        using var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
            });

        using var client = new HttpClient(handler);
        using var updateService = new UpdateService(client, currentVersionOverride: new Version(1, 1, 0));

        var result = await updateService.CheckForUpdatesAsync();

        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_HttpError_ReturnsErrorMessageGracefully()
    {
        using var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                ReasonPhrase = "Not Found"
            });

        using var client = new HttpClient(handler);
        using var updateService = new UpdateService(client, currentVersionOverride: new Version(1, 1, 0));

        var result = await updateService.CheckForUpdatesAsync();

        Assert.False(result.IsUpdateAvailable);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("404", result.ErrorMessage);
    }

    [Fact]
    public async Task VerifyChecksumAsync_MatchingHash_ReturnsTrue()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_checksum_{Guid.NewGuid():N}.bin");
        try
        {
            var content = "GotifyWin test executable content"u8.ToArray();
            await File.WriteAllBytesAsync(tempFile, content);

            var computedBytes = SHA256.HashData(content);
            var expectedHash = Convert.ToHexString(computedBytes).ToLowerInvariant();

            using var updateService = new UpdateService();
            var isValid = await updateService.VerifyChecksumAsync(tempFile, expectedHash);

            Assert.True(isValid);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task VerifyChecksumAsync_MismatchedHash_ReturnsFalse()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_checksum_mismatch_{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllBytesAsync(tempFile, "Different content"u8.ToArray());

            using var updateService = new UpdateService();
            var isValid = await updateService.VerifyChecksumAsync(tempFile, "0000000000000000000000000000000000000000000000000000000000000000");

            Assert.False(isValid);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SettingsViewModel_UpdateWorkflow_IntegratesSeamlessly()
    {
        var configService = new DummyConfigService();
        var apiService = new GotifyApiService();
        var startupManager = new DummyStartupManager();
        var presenter = new DummyPresenter();

        var releaseJson = JsonSerializer.Serialize(new GitHubReleaseDto
        {
            TagName = "v1.2.0",
            Name = "v1.2.0 - Enhanced Experience",
            Body = "Changelog details",
            HtmlUrl = "https://github.com/roscocheeta/gotify-win-client/releases/tag/v1.2.0",
            PublishedAt = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc),
            Assets = new List<GitHubReleaseAssetDto>
            {
                new()
                {
                    Name = "GotifyWin-v1.2.0-win-x64-standalone.zip",
                    Size = 1000,
                    BrowserDownloadUrl = "https://example.com/standalone.zip"
                }
            }
        });

        using var handler = new MockHttpMessageHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
            });

        using var client = new HttpClient(handler);
        using var updateService = new UpdateService(client, currentVersionOverride: new Version(1, 1, 0));

        var vm = new SettingsViewModel(configService, apiService, startupManager, presenter, updateService: updateService);

        Assert.Equal("v1.1.0", vm.CurrentAppVersion);
        Assert.False(vm.IsUpdateAvailable);

        // Toggle About
        Assert.False(vm.IsAboutExpanded);
        vm.ToggleAboutCommand.Execute(null);
        Assert.True(vm.IsAboutExpanded);

        // Check for updates
        await vm.CheckForUpdatesAsync(isManual: true);

        Assert.True(vm.IsUpdateAvailable);
        Assert.Equal("v1.2.0", vm.LatestAppVersion);
        Assert.Equal("v1.2.0 - Enhanced Experience", vm.LatestReleaseName);
        Assert.Equal("Changelog details", vm.ReleaseNotes);
        Assert.Contains("Update v1.2.0 is available", vm.UpdateStatusMessage);
        Assert.Contains("Update Available", vm.AboutSummaryText);
    }
}

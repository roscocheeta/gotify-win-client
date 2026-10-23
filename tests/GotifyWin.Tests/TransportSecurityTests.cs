using GotifyWin.Models.Config;
using GotifyWin.Services;
using GotifyWin.ViewModels;
using Xunit;

namespace GotifyWin.Tests;

public class TransportSecurityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("gotify.example.com")]
    [InlineData("ftp://gotify.example.com")]
    [InlineData("file:///C:/secrets")]
    [InlineData("https://user:pass@gotify.example.com")]
    public void ServerUrlValidator_RejectsInvalidUrls(string url)
    {
        var result = ServerUrlValidator.Validate(url);
        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("https://gotify.example.com")]
    [InlineData("https://gotify.example.com/")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    [InlineData("http://192.168.1.50")]
    [InlineData("http://10.0.0.5:8080")]
    [InlineData("http://172.20.1.1")]
    [InlineData("http://nas")]
    [InlineData("http://gotify.homelab.lan")]
    [InlineData("http://myhomelab.local:8080")]
    [InlineData("http://nas.home.arpa:8080")]
    public void ServerUrlValidator_NoWarning_ForHttpsOrLocalNetworks(string url)
    {
        var result = ServerUrlValidator.Validate(url);
        Assert.True(result.IsValid);
        Assert.Null(result.Warning);
    }

    [Theory]
    [InlineData("http://gotify.example.com")]
    [InlineData("http://8.8.8.8")]
    [InlineData("http://172.32.0.1")]
    public void ServerUrlValidator_Warns_ForPlainHttpOnPublicHosts(string url)
    {
        var result = ServerUrlValidator.Validate(url);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void BuildExportConfig_NeverIncludesClientToken_AndDoesNotMutateSource()
    {
        var config = new GotifyClientConfig
        {
            ServerUrl = "https://gotify.example.com",
            ClientToken = "super-secret-token",
            GlobalMinPriority = 4
        };

        var export = SettingsViewModel.BuildExportConfig(config);

        Assert.Equal(string.Empty, export.ClientToken);
        Assert.Equal("https://gotify.example.com", export.ServerUrl);
        Assert.Equal(4, export.GlobalMinPriority);
        Assert.Equal("super-secret-token", config.ClientToken);

        var json = System.Text.Json.JsonSerializer.Serialize(export);
        Assert.DoesNotContain("super-secret-token", json);
    }
}

using System;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using GotifyWin.ViewModels;
using Xunit;

namespace GotifyWin.Tests;

public class SettingsAndAumidTests
{
    private class DummyPresenter : INotificationPresenter
    {
#pragma warning disable CS0067
        public event EventHandler<string>? UrlOpenRequested;
#pragma warning restore CS0067
        public void Present(GotifyMessage message, GotifyClientConfig config) { }
        public void OpenUrl(string url) { }
    }

    [Fact]
    public void AumidHelper_Constants_AreValid()
    {
        Assert.Equal("GotifyWin.DesktopClient", AumidHelper.AppUserModelId);
        Assert.Equal("Gotify Desktop.lnk", AumidHelper.ShortcutFileName);
    }

    [Fact]
    public void AumidHelper_EnsureShortcut_DuringTestRun_DoesNotThrow()
    {
        var exception = Record.Exception(() => AumidHelper.EnsureShortcut());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData(NotificationMode.CustomHud, true)]
    [InlineData(NotificationMode.Both, true)]
    [InlineData(NotificationMode.NativeToast, false)]
    public void SettingsViewModel_IsHudCustomizationEnabled_MatchesMode(NotificationMode mode, bool expected)
    {
        var configService = new ConfigService();
        configService.Config.Presentation.Mode = mode;

        var vm = new SettingsViewModel(
            configService,
            new GotifyApiService(),
            new StartupManager(),
            new DummyPresenter(),
            new ThemeManager()
        );

        Assert.Equal(expected, vm.IsHudCustomizationEnabled);
    }

    [Fact]
    public void SettingsViewModel_NotificationsSummaryText_ForNativeToast_ReportsSystemManaged()
    {
        var configService = new ConfigService();
        configService.Config.Presentation.Mode = NotificationMode.NativeToast;

        var vm = new SettingsViewModel(
            configService,
            new GotifyApiService(),
            new StartupManager(),
            new DummyPresenter(),
            new ThemeManager()
        );

        Assert.Contains("System Managed", vm.NotificationsSummaryText);
    }

    [Fact]
    public void MainViewModel_AppVersionDisplay_ReturnsSemanticVersionString()
    {
        var configService = new ConfigService();
        var vm = new MainViewModel(
            configService,
            new GotifyApiService(),
            new GotifyWebSocketService(),
            new FilterEngine(),
            new DndManager(),
            new DummyPresenter(),
            new StartupManager()
        );

        Assert.NotNull(vm.AppVersionDisplay);
        Assert.StartsWith("v", vm.AppVersionDisplay);
    }
}

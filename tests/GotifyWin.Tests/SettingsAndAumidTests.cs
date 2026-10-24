using System;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using GotifyWin.ViewModels;
using Xunit;

namespace GotifyWin.Tests;

public class SettingsAndAumidTests
{
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

        var mockPresenter = new NotificationPresenter(new SoundService());
        var vm = new SettingsViewModel(
            configService,
            new GotifyApiService(),
            new StartupManager(),
            mockPresenter,
            new ThemeManager()
        );

        Assert.Equal(expected, vm.IsHudCustomizationEnabled);
    }

    [Fact]
    public void SettingsViewModel_NotificationsSummaryText_ForNativeToast_ReportsSystemManaged()
    {
        var configService = new ConfigService();
        configService.Config.Presentation.Mode = NotificationMode.NativeToast;

        var mockPresenter = new NotificationPresenter(new SoundService());
        var vm = new SettingsViewModel(
            configService,
            new GotifyApiService(),
            new StartupManager(),
            mockPresenter,
            new ThemeManager()
        );

        Assert.Contains("System Managed", vm.NotificationsSummaryText);
    }
}

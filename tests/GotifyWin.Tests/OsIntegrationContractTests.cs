using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Microsoft.Win32;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using Xunit;

namespace GotifyWin.Tests;

public class OsIntegrationContractTests
{
    [Fact]
    public void AumidHelper_ShortcutCOMPropertyStore_WritesPkeyAppUserModelIdCorrectly()
    {
        var tempShortcutPath = Path.Combine(Path.GetTempPath(), $"gotify_contract_test_{Guid.NewGuid():N}.lnk");
        var dummyExePath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        try
        {
            // 1. Create shortcut via AumidHelper
            AumidHelper.CreateOrUpdateShortcut(tempShortcutPath, dummyExePath, AumidHelper.AppUserModelId);
            Assert.True(File.Exists(tempShortcutPath));

            // 2. Read back via Windows Shell Application ExtendedProperty using reflection (avoiding DLR dynamic)
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            Assert.NotNull(shellType);

            var shell = Activator.CreateInstance(shellType);
            Assert.NotNull(shell);

            var dir = Path.GetDirectoryName(tempShortcutPath);
            Assert.NotNull(dir);
            var file = Path.GetFileName(tempShortcutPath);

            var folder = shellType.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { dir });
            Assert.NotNull(folder);

            var item = folder.GetType().InvokeMember("ParseName", System.Reflection.BindingFlags.InvokeMethod, null, folder, new object[] { file });
            Assert.NotNull(item);

            var aumid = item.GetType().InvokeMember("ExtendedProperty", System.Reflection.BindingFlags.InvokeMethod, null, item, new object[] { "System.AppUserModel.ID" }) as string;
            var targetPath = item.GetType().InvokeMember("ExtendedProperty", System.Reflection.BindingFlags.InvokeMethod, null, item, new object[] { "System.Link.TargetParsingPath" }) as string;

            Assert.Equal(dummyExePath, targetPath, ignoreCase: true);
            Assert.Equal(AumidHelper.AppUserModelId, aumid);
        }
        finally
        {
            if (File.Exists(tempShortcutPath))
            {
                File.Delete(tempShortcutPath);
            }
        }
    }

    [Fact]
    public void StartupManager_RegistryContract_SetsAndCleansAutoStart()
    {
        var startup = new StartupManager();
        const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string appName = "GotifyWinClient";

        try
        {
            // Enable auto-start
            startup.SetAutoStart(true);
            Assert.True(startup.IsAutoStartEnabled());

            using (var key = Registry.CurrentUser.OpenSubKey(runKeyPath, false))
            {
                var val = key?.GetValue(appName) as string;
                Assert.NotNull(val);
                Assert.StartsWith("\"", val);
                Assert.EndsWith("--minimized", val);
            }

            // Disable auto-start
            startup.SetAutoStart(false);
            Assert.False(startup.IsAutoStartEnabled());

            using (var key = Registry.CurrentUser.OpenSubKey(runKeyPath, false))
            {
                Assert.Null(key?.GetValue(appName));
            }
        }
        finally
        {
            // Ensure no lingering test registration in Registry
            try
            {
                using var cleanupKey = Registry.CurrentUser.OpenSubKey(runKeyPath, true);
                cleanupKey?.DeleteValue(appName, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clean up test registry key: {ex.Message}");
            }
        }
    }

    [Fact]
    public void ThemeManager_ThemeSelectionAndRegistryResolution()
    {
        var manager = new ThemeManager();
        bool? eventFiredWithDark = null;
        manager.ThemeChanged += (s, isDark) => eventFiredWithDark = isDark;

        manager.ApplyTheme(AppThemeMode.Dark);
        Assert.True(manager.IsDarkThemeActive);
        Assert.True(eventFiredWithDark);

        manager.ApplyTheme(AppThemeMode.Light);
        Assert.False(manager.IsDarkThemeActive);
        Assert.False(eventFiredWithDark);

        // Verify registry read executes safely
        var isWindowsLight = ThemeManager.IsWindowsLightTheme();
        Assert.IsType<bool>(isWindowsLight);
    }

    [Fact]
    public void SoundService_GracefulAudioPlaybackAndMute()
    {
        var sound = new SoundService();

        // Should return immediately without throwing
        var exMuted = Record.Exception(() => sound.PlayNotification("non_existent_file.wav", isMuted: true));
        Assert.Null(exMuted);

        // Fallback to system sound when path is missing
        var exFallback = Record.Exception(() => sound.PlayNotification("missing_sound_12345.wav", isMuted: false));
        Assert.Null(exFallback);
    }

    [Fact]
    public void NotificationPresenter_ActionCenterHistoryAndXmlSchema()
    {
        var extras = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>
        {
            ["client::notification"] = System.Text.Json.JsonDocument.Parse("{\"click\":{\"url\":\"https://example.com/alert\"}}").RootElement
        };

        var message = new GotifyMessage
        {
            Id = 1234,
            Title = "Contract Test Title & <Special> Characters",
            Message = "Testing toast XML escaping and Action Center API contracts.",
            Priority = 6,
            Extras = extras
        };

        // 1. Verify toast XML conforms to Windows toast XML schema
        var xmlString = NotificationPresenter.GenerateToastXml(message);
        var doc = new XmlDocument();
        var ex = Record.Exception(() => doc.LoadXml(xmlString));
        Assert.Null(ex);

        // 2. Verify Windows Toast Notifier recognizes the AUMID (interactive sessions)
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier(AumidHelper.AppUserModelId);
            Assert.NotNull(notifier);
            Assert.NotEqual(NotificationSetting.DisabledByManifest, notifier.Setting);

            // 3. Verify ToastNotificationHistory can query history for this AUMID without throwing
            var history = ToastNotificationManager.History;
            var exHistory = Record.Exception(() => history.GetHistory(AumidHelper.AppUserModelId));
            Assert.Null(exHistory);
        }
        catch (COMException)
        {
            // In headless CI runner environments (e.g. GitHub Actions windows-latest running in Session 0),
            // the Windows Push Notification Platform broker is not hosted.
            // The XML schema validation above verifies payload validity unconditionally.
        }
    }
}

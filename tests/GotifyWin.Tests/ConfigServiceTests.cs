using System.IO;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using Xunit;

namespace GotifyWin.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void Load_WhenConfigFileMissing_GeneratesDefaultsAndSaves()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"gotify_test_{Guid.NewGuid():N}.json");

        try
        {
            var service = new ConfigService(tempFile);
            service.Load();

            Assert.NotNull(service.Config);
            Assert.True(File.Exists(tempFile));
            Assert.Equal(NotificationMode.CustomHud, service.Config.Presentation.Mode);
            Assert.Equal(5.0, service.Config.Presentation.DisplayDurationSeconds);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public void SaveAndLoad_ProtectsAndUnprotectsTokenWithDpapi()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"gotify_dpapi_{Guid.NewGuid():N}.json");

        try
        {
            var service = new ConfigService(tempFile);
            service.Config.ServerUrl = "https://gotify.example.com";
            service.Config.ClientToken = "my-secret-client-token-12345";
            service.Save();

            // Verify the raw JSON on disk contains DPAPI protected token "enc:"
            var rawJson = File.ReadAllText(tempFile);
            Assert.DoesNotContain("my-secret-client-token-12345", rawJson);
            Assert.Contains("enc:", rawJson);

            // Now load with new instance
            var loaderService = new ConfigService(tempFile);
            loaderService.Load();

            Assert.Equal("my-secret-client-token-12345", loaderService.Config.ClientToken);
            Assert.Equal("https://gotify.example.com", loaderService.Config.ServerUrl);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public void SaveAndLoad_PreservesAppFiltersAndRules()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"gotify_complex_{Guid.NewGuid():N}.json");

        try
        {
            var service = new ConfigService(tempFile);
            service.Config.AppFilters[42] = new AppFilterSetting
            {
                AppId = 42,
                AppName = "Home Assistant",
                Enabled = true,
                MinPriority = 4,
                OverrideGlobalDnd = true
            };

            service.Config.Rules.Add(new NotificationRule
            {
                Name = "Drop Backups",
                TargetField = RuleField.Title,
                Operator = RuleOperator.Contains,
                Pattern = "Backup",
                Action = RuleAction.Drop
            });

            service.Save();

            var loaded = new ConfigService(tempFile);
            loaded.Load();

            Assert.True(loaded.Config.AppFilters.ContainsKey(42));
            Assert.Equal("Home Assistant", loaded.Config.AppFilters[42].AppName);
            Assert.True(loaded.Config.AppFilters[42].OverrideGlobalDnd);
            Assert.Single(loaded.Config.Rules);
            Assert.Equal("Drop Backups", loaded.Config.Rules[0].Name);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}

using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using Xunit;

namespace GotifyWin.Tests;

public class NotificationPatternAnalyzerTests
{
    private readonly NotificationPatternAnalyzer _analyzer = new();

    [Theory]
    [InlineData("Backup finished at 2026-10-22 14:05:00 in 14.2s", "Backup finished at <DATE> in <NUM>")]
    [InlineData("Grabbed Severance S02E01 in 1080p x265", "Grabbed Severance S02E01 in <NUM> <CODEC>")]
    [InlineData("Container 4a8b7c9d1e2f updated", "Container <ID> updated")]
    [InlineData("Server at https://gotify.homelab.lan:8080 is reachable", "Server at <URL> is reachable")]
    [InlineData("Host 192.168.1.50 ping: 12ms", "Host <IP> ping: <NUM>")]
    [InlineData("Saved file 'snapshot_report.pdf' (1.4MB)", "Saved file <STR> (<NUM>)")]
    public void MaskVariables_NormalizesDynamicEntities(string input, string expected)
    {
        var result = NotificationPatternAnalyzer.MaskVariables(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Analyze_WhenFewerThanThreshold_ReturnsNoRecommendations()
    {
        var config = new GotifyClientConfig();
        var messages = new List<GotifyMessage>();

        for (int i = 1; i <= 4; i++) // Threshold is 5
        {
            messages.Add(new GotifyMessage
            {
                Id = i,
                AppId = 2,
                ApplicationName = "Sonarr",
                Title = "Episode Downloaded",
                Message = $"Episode #{i} downloaded in 1080p",
                Priority = 3
            });
        }

        var results = _analyzer.Analyze(messages, config, threshold: 5);
        Assert.Empty(results);
    }

    [Fact]
    public void Analyze_WhenFiveRepetitiveMessages_GeneratesSilentLogRecommendation()
    {
        var config = new GotifyClientConfig();
        var messages = new List<GotifyMessage>();

        for (int i = 1; i <= 5; i++)
        {
            messages.Add(new GotifyMessage
            {
                Id = i,
                AppId = 7,
                ApplicationName = "Notion-Backup",
                Title = "Backup Completed",
                Message = $"Backup workspace {i} finished in {10 + i}s",
                Priority = 3
            });
        }

        var results = _analyzer.Analyze(messages, config, threshold: 5);

        Assert.Single(results);
        var rec = results[0];
        Assert.Equal(7, rec.AppId);
        Assert.Equal("Notion-Backup", rec.AppName);
        Assert.Equal(RuleField.Title, rec.SuggestedField);
        Assert.Equal("Backup Completed", rec.SuggestedPattern);
        Assert.Equal(RuleAction.SilentLog, rec.SuggestedAction);
        Assert.Equal(5, rec.MatchCount);
    }

    [Fact]
    public void Analyze_WhenMessagesContainErrorKeywords_SkipsRecommendationForSafety()
    {
        var config = new GotifyClientConfig();
        var messages = new List<GotifyMessage>();

        for (int i = 1; i <= 6; i++)
        {
            messages.Add(new GotifyMessage
            {
                Id = i,
                AppId = 10,
                ApplicationName = "Home Assistant",
                Title = "Node offline error",
                Message = $"Device {i} failed health check",
                Priority = 4
            });
        }

        var results = _analyzer.Analyze(messages, config, threshold: 5);
        Assert.Empty(results);
    }

    [Fact]
    public void Analyze_WhenPrioritySevenOrHigher_SkipsRecommendationForSafety()
    {
        var config = new GotifyClientConfig();
        var messages = new List<GotifyMessage>();

        for (int i = 1; i <= 6; i++)
        {
            messages.Add(new GotifyMessage
            {
                Id = i,
                AppId = 1,
                ApplicationName = "Proxmox",
                Title = "Storage Alert",
                Message = $"Pool scrub {i} notice",
                Priority = 8 // High priority
            });
        }

        var results = _analyzer.Analyze(messages, config, threshold: 5);
        Assert.Empty(results);
    }

    [Fact]
    public void Analyze_WhenAlreadyFilteredByExistingRule_SkipsRecommendation()
    {
        var config = new GotifyClientConfig();
        config.Rules.Add(new NotificationRule
        {
            Name = "Mute Sonarr downloads",
            Enabled = true,
            TargetField = RuleField.Title,
            Operator = RuleOperator.Contains,
            Pattern = "Episode Downloaded",
            Action = RuleAction.SilentLog
        });

        var messages = new List<GotifyMessage>();
        for (int i = 1; i <= 7; i++)
        {
            messages.Add(new GotifyMessage
            {
                Id = i,
                AppId = 2,
                ApplicationName = "Sonarr",
                Title = "Episode Downloaded",
                Message = $"Severance S01E0{i}",
                Priority = 3
            });
        }

        var results = _analyzer.Analyze(messages, config, threshold: 5);
        Assert.Empty(results);
    }

    [Fact]
    public void Analyze_WhenAppIsDisabledByIdInAppFilters_SkipsRecommendation()
    {
        var config = new GotifyClientConfig();
        // User muted Profilarr in the Apps tab
        config.AppFilters[15] = new AppFilterSetting
        {
            AppId = 15,
            AppName = "Profilarr",
            Enabled = false
        };

        var messages = new List<GotifyMessage>();
        for (int i = 1; i <= 6; i++)
        {
            messages.Add(new GotifyMessage
            {
                Id = i,
                AppId = 15,
                ApplicationName = "Profilarr",
                Title = "Profile Sync Complete",
                Message = $"Synced media profiles batch #{i}",
                Priority = 3
            });
        }

        var results = _analyzer.Analyze(messages, config, threshold: 5);
        Assert.Empty(results);
    }

    [Fact]
    public void Analyze_WhenAppIsDisabledByNameInAppFilters_SkipsRecommendation()
    {
        var config = new GotifyClientConfig();
        config.AppFilters[99] = new AppFilterSetting
        {
            AppId = 99,
            AppName = "Profilarr",
            Enabled = false
        };

        var messages = new List<GotifyMessage>();
        for (int i = 1; i <= 6; i++)
        {
            messages.Add(new GotifyMessage
            {
                Id = i,
                AppId = 0,
                ApplicationName = "Profilarr",
                Title = "Profile Sync Complete",
                Message = $"Synced media profiles batch #{i}",
                Priority = 3
            });
        }

        var results = _analyzer.Analyze(messages, config, threshold: 5);
        Assert.Empty(results);
    }

    [Fact]
    public void Analyze_WhenAppMessagesBelowMinPriorityThreshold_SkipsRecommendation()
    {
        var config = new GotifyClientConfig();
        config.AppFilters[15] = new AppFilterSetting
        {
            AppId = 15,
            AppName = "Profilarr",
            Enabled = true,
            MinPriority = 5
        };

        var messages = new List<GotifyMessage>();
        for (int i = 1; i <= 6; i++)
        {
            messages.Add(new GotifyMessage
            {
                Id = i,
                AppId = 15,
                ApplicationName = "Profilarr",
                Title = "Routine Sync Ping",
                Message = $"Ping #{i}",
                Priority = 2
            });
        }

        var results = _analyzer.Analyze(messages, config, threshold: 5);
        Assert.Empty(results);
    }
}
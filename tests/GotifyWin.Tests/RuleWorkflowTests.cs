using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using GotifyWin.ViewModels;
using Xunit;

namespace GotifyWin.Tests;

public class RuleWorkflowTests
{
    private class DummyConfigService : IConfigService
    {
        public GotifyClientConfig Config { get; set; } = new();
        public string ConfigFilePath => string.Empty;
        public event EventHandler<GotifyClientConfig>? ConfigChanged;
        public void Load() { }
        public void Save() { ConfigChanged?.Invoke(this, Config); }
    }

    [Fact]
    public void PrepopulateFromMessage_WithTitle_FillsTitleAndPattern()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        var msg = new GotifyMessage
        {
            Id = 100,
            AppId = 2,
            ApplicationName = "Sonarr",
            Title = "Episode Downloaded",
            Message = "Series X S01E01 downloaded in 1080p",
            Priority = 4
        };

        rulesVm.PrepopulateFromMessage(msg);

        Assert.Equal(RuleField.Title, rulesVm.NewRuleField);
        Assert.Equal(RuleOperator.Contains, rulesVm.NewRuleOperator);
        Assert.Equal("Episode Downloaded", rulesVm.NewRulePattern);
        Assert.Equal(RuleAction.SilentLog, rulesVm.NewRuleAction);
        Assert.Contains("Sonarr", rulesVm.NewRuleName);
    }

    [Fact]
    public void PrepopulateFromMessage_WithoutTitle_FallsBackToMessage()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        var msg = new GotifyMessage
        {
            Id = 101,
            AppId = 5,
            ApplicationName = "Home Assistant",
            Title = "",
            Message = "Front door opened",
            Priority = 3
        };

        rulesVm.PrepopulateFromMessage(msg);

        Assert.Equal(RuleField.Message, rulesVm.NewRuleField);
        Assert.Equal(RuleOperator.Contains, rulesVm.NewRuleOperator);
        Assert.Equal("Front door opened", rulesVm.NewRulePattern);
    }

    [Fact]
    public void FeedViewModel_CreateRuleCommand_FiresCreateRuleRequested()
    {
        var configService = new DummyConfigService();
        var feedVm = new FeedViewModel(null!, configService);

        var msg = new GotifyMessage
        {
            Id = 102,
            Title = "Disk Low Space",
            Message = "Drive D: is 92% full"
        };

        GotifyMessage? received = null;
        feedVm.CreateRuleRequested += (s, m) => received = m;

        feedVm.CreateRuleCommand.Execute(msg);

        Assert.NotNull(received);
        Assert.Equal("Disk Low Space", received.Title);
    }

    [Fact]
    public void ApplyRecommendation_AddsRuleAndRemovesRecommendation()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        var rec = new RuleRecommendation
        {
            AppId = 8,
            AppName = "Bazarr",
            Title = "Mute routine Bazarr alerts",
            SuggestedField = RuleField.Title,
            SuggestedOperator = RuleOperator.Contains,
            SuggestedPattern = "Subtitle Downloaded",
            SuggestedAction = RuleAction.SilentLog,
            MatchCount = 7
        };

        rulesVm.SetRecommendations(new[] { rec });
        Assert.True(rulesVm.HasRecommendations);
        Assert.Single(rulesVm.Recommendations);

        rulesVm.ApplyRecommendation(rec, RuleAction.SilentLog);

        Assert.False(rulesVm.HasRecommendations);
        Assert.Single(rulesVm.Rules);
        Assert.Equal("Subtitle Downloaded", rulesVm.Rules[0].Pattern);
        Assert.Equal(RuleAction.SilentLog, rulesVm.Rules[0].Action);
        Assert.Single(configService.Config.Rules);
    }

    [Fact]
    public void DismissRecommendation_RemovesRecommendationWithoutAddingRule()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        var rec = new RuleRecommendation
        {
            AppId = 8,
            AppName = "Bazarr",
            Title = "Mute routine Bazarr alerts"
        };

        rulesVm.SetRecommendations(new[] { rec });
        Assert.True(rulesVm.HasRecommendations);

        rulesVm.DismissRecommendation(rec);

        Assert.False(rulesVm.HasRecommendations);
        Assert.Empty(rulesVm.Rules);
    }

    [Fact]
    public void FilterEngine_WhenMatchedSilentLogRule_SetsFilteredByRuleName()
    {
        var engine = new FilterEngine();
        var config = new GotifyClientConfig();
        config.Rules.Add(new NotificationRule
        {
            Name = "Ignore Health Check",
            Enabled = true,
            TargetField = RuleField.Title,
            Operator = RuleOperator.Contains,
            Pattern = "HealthCheck",
            Action = RuleAction.SilentLog
        });

        var msg = new GotifyMessage
        {
            Id = 555,
            AppId = 1,
            Title = "HealthCheck OK",
            Message = "All systems operational",
            Priority = 3
        };

        var result = engine.Evaluate(msg, config, isDndActive: false);

        Assert.Equal(FilterDecision.SilentLog, result.Decision);
        Assert.True(msg.IsFiltered);
        Assert.Equal("Ignore Health Check", msg.FilteredByRuleName);
    }

    [Fact]
    public async Task ReanalyzeHistory_WhenHistoryInsufficient_RetainsExistingRecommendations()
    {
        var configService = new DummyConfigService();
        var analyzer = new NotificationPatternAnalyzer();
        var currentMessages = new List<GotifyMessage>(); // Empty history (e.g. cleared by user)

        var rulesVm = new RulesViewModel(configService, analyzer, () => currentMessages);

        var existingRec = new RuleRecommendation
        {
            AppId = 7,
            AppName = "Notion-Backup",
            Title = "Mute routine Notion-Backup alerts",
            MatchCount = 10
        };

        rulesVm.SetRecommendations(new[] { existingRec });
        Assert.True(rulesVm.HasRecommendations);

        // Act: re-scan when history is cleared (< 5 items)
        await rulesVm.ReanalyzeHistoryAsync();

        // Assert: must NOT clear the existing recommendation!
        Assert.True(rulesVm.HasRecommendations);
        Assert.Single(rulesVm.Recommendations);
        Assert.Equal("Mute routine Notion-Backup alerts", rulesVm.Recommendations[0].Title);
    }

    [Fact]
    public void PrepareManualRuleFromNotification_CollapsesOtherSectionsAndExpandsAdvanced()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        rulesVm.IsRecommendationsExpanded = true;
        rulesVm.IsConfiguredRulesExpanded = true;
        rulesVm.IsAdvancedExpanded = false;

        var msg = new GotifyMessage
        {
            Id = 888,
            AppId = 4,
            ApplicationName = "Profiler",
            Title = "High CPU Detected",
            Message = "CPU usage at 99% for 5 minutes",
            Priority = 6
        };

        rulesVm.PrepareManualRuleFromNotification(msg);

        // Sections must be collapsed except Advanced
        Assert.False(rulesVm.IsRecommendationsExpanded);
        Assert.False(rulesVm.IsConfiguredRulesExpanded);
        Assert.True(rulesVm.IsAdvancedExpanded);

        // Advanced fields must be prefilled
        Assert.Equal("Filter Profiler: High CPU Detected", rulesVm.NewRuleName);
        Assert.Equal(RuleField.Title, rulesVm.NewRuleField);
        Assert.Equal("High CPU Detected", rulesVm.NewRulePattern);
    }

    [Fact]
    public void RulesSummaryText_ReflectsActiveAndPausedCounts()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        Assert.Equal("0 rules", rulesVm.RulesSummaryText);

        var r1 = new NotificationRule { Name = "Rule 1", Enabled = true };
        var r2 = new NotificationRule { Name = "Rule 2", Enabled = false };

        configService.Config.Rules.Add(r1);
        configService.Config.Rules.Add(r2);

        // Re-init VM or add via VM
        var rulesVm2 = new RulesViewModel(configService);

        Assert.Equal(1, rulesVm2.ActiveRulesCount);
        Assert.Equal(1, rulesVm2.PausedRulesCount);
        Assert.Equal("1 active • 1 paused", rulesVm2.RulesSummaryText);

        // Toggle r2 to active
        r2.Enabled = true;
        Assert.Equal(2, rulesVm2.ActiveRulesCount);
        Assert.Equal(0, rulesVm2.PausedRulesCount);
        Assert.Equal("2 active", rulesVm2.RulesSummaryText);
    }

    [Fact]
    public void NotificationRule_StatusLabel_UpdatesOnEnableToggle()
    {
        var rule = new NotificationRule { Name = "Backup Warning", Enabled = true };
        Assert.Equal("Active", rule.StatusLabel);

        rule.Enabled = false;
        Assert.Equal("Paused", rule.StatusLabel);
    }

    [Fact]
    public void FeedViewModel_HidesMessagesWhenAppIsDisabled()
    {
        var configService = new DummyConfigService();
        var feedVm = new FeedViewModel(null!, configService);

        var msgAllowed = new GotifyMessage
        {
            Id = 1,
            AppId = 10,
            ApplicationName = "Docker",
            Title = "Container restarted",
            Message = "redis restarted"
        };
        var msgDisabledApp = new GotifyMessage
        {
            Id = 2,
            AppId = 20,
            ApplicationName = "Profiler",
            Title = "Trace collected",
            Message = "Profile trace #402"
        };

        // Disable Profiler app (AppId 20)
        configService.Config.AppFilters[20] = new AppFilterSetting
        {
            AppId = 20,
            AppName = "Profiler",
            Enabled = false
        };

        feedVm.SetInitialMessages(new[] { msgAllowed, msgDisabledApp });

        // FilteredMessages should only contain the allowed message
        var items = feedVm.FilteredMessages.Cast<GotifyMessage>().ToList();
        Assert.Single(items);
        Assert.Equal("Docker", items[0].ApplicationName);
    }

    [Fact]
    public void SettingsViewModel_GlobalMinPriority_UpdatesConfigAndFormatsText()
    {
        var configService = new DummyConfigService();
        var settingsVm = new SettingsViewModel(configService, null!, null!, null!);

        settingsVm.GlobalMinPriority = 0;
        Assert.Equal("All Alerts (0+)", settingsVm.GlobalMinPriorityText);
        Assert.Equal(0, configService.Config.GlobalMinPriority);

        settingsVm.GlobalMinPriority = 6;
        Assert.Equal("Priority \u2265 6 (Normal)", settingsVm.GlobalMinPriorityText);
        Assert.Equal(6, configService.Config.GlobalMinPriority);

        settingsVm.GlobalMinPriority = 8;
        Assert.Equal("Priority \u2265 8 (Urgent)", settingsVm.GlobalMinPriorityText);
        Assert.Equal(8, configService.Config.GlobalMinPriority);
    }

    [Fact]
    public async Task RulesViewModel_Reanalyze_TransitionsButtonStateProperly()
    {
        var configService = new DummyConfigService();
        var analyzer = new NotificationPatternAnalyzer();
        var rulesVm = new RulesViewModel(configService, analyzer, () => Enumerable.Empty<GotifyMessage>());

        Assert.Equal("⚡ Re-scan History", rulesVm.ReanalyzeButtonText);
        Assert.True(rulesVm.CanReanalyze);

        var task = rulesVm.ReanalyzeHistoryAsync();
        // Right after start, button indicates scanning
        Assert.True(rulesVm.IsReanalyzing);
        Assert.Equal("⏳ Scanning...", rulesVm.ReanalyzeButtonText);

        await task;

        // After completion, resets to initial state
        Assert.False(rulesVm.IsReanalyzing);
        Assert.Equal("⚡ Re-scan History", rulesVm.ReanalyzeButtonText);
    }
}

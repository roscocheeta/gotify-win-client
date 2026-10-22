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
}

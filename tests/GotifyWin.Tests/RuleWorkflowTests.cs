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
        Assert.Equal(RuleAction.Drop, rulesVm.NewRuleAction);
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
}

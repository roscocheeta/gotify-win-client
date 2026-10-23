using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using Xunit;

namespace GotifyWin.Tests;

public class FilterEngineTests
{
    private readonly FilterEngine _engine = new();

    private GotifyClientConfig CreateDefaultConfig()
    {
        return new GotifyClientConfig
        {
            GlobalMinPriority = 2,
            Presentation = new PresentationSettings
            {
                EnableDebounce = true,
                DebounceWindowSeconds = 5
            },
            Dnd = new DndSettings
            {
                IsManualDndActive = false,
                EnableEmergencyBypass = true,
                EmergencyBypassPriority = 8
            }
        };
    }

    [Fact]
    public void Evaluate_WhenAppIsDisabled_ReturnsDrop()
    {
        var config = CreateDefaultConfig();
        config.AppFilters[10] = new AppFilterSetting
        {
            AppId = 10,
            AppName = "Disabled App",
            Enabled = false
        };

        var message = new GotifyMessage
        {
            AppId = 10,
            Title = "Alert",
            Message = "Test message",
            Priority = 5
        };

        var result = _engine.Evaluate(message, config, isDndActive: false);

        Assert.Equal(FilterDecision.Drop, result.Decision);
        Assert.Contains("disabled", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_WhenAppPriorityBelowThreshold_ReturnsSilentLog()
    {
        var config = CreateDefaultConfig();
        config.AppFilters[5] = new AppFilterSetting
        {
            AppId = 5,
            AppName = "Monitoring",
            Enabled = true,
            MinPriority = 6
        };

        var message = new GotifyMessage
        {
            AppId = 5,
            Title = "Minor event",
            Message = "Low priority notice",
            Priority = 4
        };

        var result = _engine.Evaluate(message, config, isDndActive: false);

        Assert.Equal(FilterDecision.SilentLog, result.Decision);
        Assert.Contains("below app threshold", result.Reason);
    }

    [Fact]
    public void Evaluate_WhenGlobalPriorityBelowThreshold_ReturnsSilentLog()
    {
        var config = CreateDefaultConfig();
        config.GlobalMinPriority = 4;

        var message = new GotifyMessage
        {
            AppId = 99,
            Title = "Heartbeat",
            Message = "All good",
            Priority = 2
        };

        var result = _engine.Evaluate(message, config, isDndActive: false);

        Assert.Equal(FilterDecision.SilentLog, result.Decision);
        Assert.Contains("below global threshold", result.Reason);
    }

    [Fact]
    public void Evaluate_WhenCustomRuleMatchesDrop_ReturnsDrop()
    {
        var config = CreateDefaultConfig();
        config.Rules.Add(new NotificationRule
        {
            Name = "Drop Routine Backups",
            TargetField = RuleField.Title,
            Operator = RuleOperator.Contains,
            Pattern = "[HEALTHCHECK]",
            Action = RuleAction.Drop,
            Enabled = true
        });

        var message = new GotifyMessage
        {
            AppId = 1,
            Title = "Database [HEALTHCHECK] Passed",
            Message = "All 12 tables healthy",
            Priority = 5
        };

        var result = _engine.Evaluate(message, config, isDndActive: false);

        Assert.Equal(FilterDecision.Drop, result.Decision);
        Assert.Contains("Drop Routine Backups", result.Reason);
    }

    [Fact]
    public void Evaluate_WhenCustomRegexRuleMatches_CorrectlyAppliesAction()
    {
        var config = CreateDefaultConfig();
        config.Rules.Add(new NotificationRule
        {
            Name = "Regex Backup Completed",
            TargetField = RuleField.Message,
            Operator = RuleOperator.RegexMatch,
            Pattern = @"Backup job \d+ finished successfully",
            Action = RuleAction.SilentLog,
            Enabled = true
        });

        var message = new GotifyMessage
        {
            AppId = 1,
            Title = "Backup Report",
            Message = "Backup job 42 finished successfully in 12s",
            Priority = 6
        };

        var result = _engine.Evaluate(message, config, isDndActive: false);

        Assert.Equal(FilterDecision.SilentLog, result.Decision);
    }

    [Fact]
    public void Evaluate_WhenCustomRuleIsAppScoped_MatchesTargetAppOnly()
    {
        var config = CreateDefaultConfig();
        // Scoped specifically to Sonarr (AppId: 2)
        config.Rules.Add(new NotificationRule
        {
            Name = "Silence Sonarr Import",
            AppFilterId = 2,
            AppFilterName = "Sonarr",
            TargetField = RuleField.Title,
            Operator = RuleOperator.Contains,
            Pattern = "Import Complete",
            Action = RuleAction.SilentLog,
            Enabled = true
        });

        // Message 1 from Sonarr: should match rule and be Silently Logged
        var sonarrMsg = new GotifyMessage
        {
            AppId = 2,
            ApplicationName = "Sonarr",
            Title = "Import Complete: Series S01E01",
            Priority = 5
        };
        var sonarrResult = _engine.Evaluate(sonarrMsg, config, isDndActive: false);
        Assert.Equal(FilterDecision.SilentLog, sonarrResult.Decision);
        Assert.Contains("Silence Sonarr Import", sonarrResult.Reason);

        // Message 2 from Radarr: same pattern but different app, must NOT be silenced by the rule
        var radarrMsg = new GotifyMessage
        {
            AppId = 3,
            ApplicationName = "Radarr",
            Title = "Import Complete: Movie (2024)",
            Priority = 5
        };
        var radarrResult = _engine.Evaluate(radarrMsg, config, isDndActive: false);
        Assert.Equal(FilterDecision.Alert, radarrResult.Decision);
    }

    [Fact]
    public void Evaluate_WhenCustomRuleHasNoAppScope_MatchesAcrossAllApps()
    {
        var config = CreateDefaultConfig();
        // Global scope (AppFilterId is null)
        config.Rules.Add(new NotificationRule
        {
            Name = "Global Mute",
            AppFilterId = null,
            TargetField = RuleField.Title,
            Operator = RuleOperator.Contains,
            Pattern = "Heartbeat",
            Action = RuleAction.Drop,
            Enabled = true
        });

        var app1Msg = new GotifyMessage { AppId = 1, Title = "Heartbeat Check", Priority = 5 };
        var app2Msg = new GotifyMessage { AppId = 2, Title = "Node Heartbeat", Priority = 5 };

        Assert.Equal(FilterDecision.Drop, _engine.Evaluate(app1Msg, config, isDndActive: false).Decision);
        Assert.Equal(FilterDecision.Drop, _engine.Evaluate(app2Msg, config, isDndActive: false).Decision);
    }

    [Fact]
    public void Evaluate_InvalidRegexPattern_FailsGracefullyWithoutThrowing()
    {
        var config = CreateDefaultConfig();
        config.Rules.Add(new NotificationRule
        {
            Name = "Broken Regex Rule",
            TargetField = RuleField.Title,
            Operator = RuleOperator.RegexMatch,
            Pattern = @"[unclosed-bracket",
            Action = RuleAction.Drop,
            Enabled = true
        });

        var message = new GotifyMessage
        {
            AppId = 1,
            Title = "Some title",
            Message = "Some message",
            Priority = 5
        };

        var result = _engine.Evaluate(message, config, isDndActive: false);

        Assert.Equal(FilterDecision.Alert, result.Decision);
    }

    [Fact]
    public void Evaluate_DebounceSuppressesDuplicates_WithinWindow()
    {
        _engine.ClearDebounceCache();
        var config = CreateDefaultConfig();
        config.Presentation.DebounceWindowSeconds = 10;

        var msg1 = new GotifyMessage
        {
            AppId = 2,
            Title = "Host DOWN",
            Message = "Server 10.0.0.5 is unreachable",
            Priority = 7
        };

        var msg2 = new GotifyMessage
        {
            AppId = 2,
            Title = "Host DOWN",
            Message = "Server 10.0.0.5 is unreachable",
            Priority = 7
        };

        var firstResult = _engine.Evaluate(msg1, config, isDndActive: false);
        var secondResult = _engine.Evaluate(msg2, config, isDndActive: false);

        Assert.Equal(FilterDecision.Alert, firstResult.Decision);
        Assert.Equal(FilterDecision.SilentLog, secondResult.Decision);
        Assert.True(secondResult.IsDuplicate);
        Assert.Equal(2, secondResult.DuplicateCount);
    }

    [Fact]
    public void Evaluate_WhenDndActiveAndPriorityBelowBypass_ReturnsSilentLog()
    {
        var config = CreateDefaultConfig();
        config.Dnd.EmergencyBypassPriority = 8;
        config.Dnd.EnableEmergencyBypass = true;

        var message = new GotifyMessage
        {
            AppId = 1,
            Title = "Normal update",
            Message = "Container restart notice",
            Priority = 6
        };

        var result = _engine.Evaluate(message, config, isDndActive: true);

        Assert.Equal(FilterDecision.SilentLog, result.Decision);
        Assert.Contains("Do Not Disturb", result.Reason);
    }

    [Fact]
    public void Evaluate_WhenDndActiveAndPriorityAboveBypass_ReturnsAlertBreakthrough()
    {
        var config = CreateDefaultConfig();
        config.Dnd.EmergencyBypassPriority = 8;
        config.Dnd.EnableEmergencyBypass = true;

        var message = new GotifyMessage
        {
            AppId = 1,
            Title = "CRITICAL: WATER LEAK DETECTED",
            Message = "Sensor kitchen-sink reporting water presence",
            Priority = 9
        };

        var result = _engine.Evaluate(message, config, isDndActive: true);

        Assert.Equal(FilterDecision.Alert, result.Decision);
        Assert.Contains("Emergency breakthrough", result.Reason);
    }

    [Fact]
    public void Evaluate_WhenAppHasOverrideGlobalDnd_ReturnsAlertUnderDnd()
    {
        var config = CreateDefaultConfig();
        config.AppFilters[7] = new AppFilterSetting
        {
            AppId = 7,
            AppName = "Plex Down Alert",
            Enabled = true,
            OverrideGlobalDnd = true
        };

        var message = new GotifyMessage
        {
            AppId = 7,
            Title = "Plex Crashed",
            Message = "Process exited with code 1",
            Priority = 5
        };

        var result = _engine.Evaluate(message, config, isDndActive: true);

        Assert.Equal(FilterDecision.Alert, result.Decision);
        Assert.Contains("override", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}

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
        Assert.NotNull(rulesVm.SelectedAppFilterOption);
        Assert.Equal(2, rulesVm.SelectedAppFilterOption.Id);
        Assert.Equal("Sonarr", rulesVm.SelectedAppFilterOption.Name);
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
        Assert.Equal(8, rulesVm.Rules[0].AppFilterId);
        Assert.Equal("Bazarr", rulesVm.Rules[0].AppFilterName);
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

    [Fact]
    public void PrepopulateFromMessage_SetsAppScopeAndPrefillsRule()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        var msg = new GotifyMessage
        {
            Id = 1001,
            AppId = 2,
            ApplicationName = "Sonarr",
            Title = "Import Complete",
            Message = "Episode imported successfully"
        };

        rulesVm.PrepopulateFromMessage(msg);

        Assert.NotNull(rulesVm.SelectedAppFilterOption);
        Assert.Equal(2, rulesVm.SelectedAppFilterOption.Id);
        Assert.Equal("Sonarr", rulesVm.SelectedAppFilterOption.Name);
        Assert.Equal("Import Complete", rulesVm.NewRulePattern);

        // Add the rule via command
        rulesVm.AddRuleCommand.Execute(null);

        Assert.Single(rulesVm.Rules);
        var created = rulesVm.Rules[0];
        Assert.Equal(2, created.AppFilterId);
        Assert.Equal("Sonarr", created.AppFilterName);
        Assert.Equal("Sonarr", created.ScopeDescription);
        Assert.Equal("Import Complete", created.Pattern);
    }

    [Fact]
    public void FilterEngine_AppScopedRule_MatchesTargetAppOnly()
    {
        var engine = new FilterEngine();
        var config = new GotifyClientConfig();

        // Rule scoped strictly to Sonarr (AppId 2)
        config.Rules.Add(new NotificationRule
        {
            Name = "Mute Sonarr Import Complete",
            Enabled = true,
            AppFilterId = 2,
            AppFilterName = "Sonarr",
            TargetField = RuleField.Title,
            Operator = RuleOperator.Contains,
            Pattern = "Import Complete",
            Action = RuleAction.SilentLog
        });

        // Message 1: From Sonarr (AppId 2) with "Import Complete"
        var sonarrMsg = new GotifyMessage
        {
            Id = 201,
            AppId = 2,
            ApplicationName = "Sonarr",
            Title = "Import Complete",
            Message = "Show X downloaded and imported",
            Priority = 5
        };
        var result1 = engine.Evaluate(sonarrMsg, config, isDndActive: false);
        Assert.Equal(FilterDecision.SilentLog, result1.Decision);
        Assert.True(sonarrMsg.IsFiltered);
        Assert.Equal("Mute Sonarr Import Complete", sonarrMsg.FilteredByRuleName);

        // Message 2: From Radarr (AppId 3) with identical title "Import Complete"
        var radarrMsg = new GotifyMessage
        {
            Id = 202,
            AppId = 3,
            ApplicationName = "Radarr",
            Title = "Import Complete",
            Message = "Movie Y downloaded and imported",
            Priority = 5
        };
        var result2 = engine.Evaluate(radarrMsg, config, isDndActive: false);
        // Must NOT be dropped or muted because the rule is scoped only to Sonarr!
        Assert.Equal(FilterDecision.Alert, result2.Decision);
        Assert.False(radarrMsg.IsFiltered);
    }

    [Fact]
    public void FilterEngine_GlobalRule_MatchesAnyApp()
    {
        var engine = new FilterEngine();
        var config = new GotifyClientConfig();

        // Unscoped global rule (AppFilterId = null)
        config.Rules.Add(new NotificationRule
        {
            Name = "Mute All Test Alerts",
            Enabled = true,
            AppFilterId = null,
            AppFilterName = null,
            TargetField = RuleField.Title,
            Operator = RuleOperator.Contains,
            Pattern = "Test Alert",
            Action = RuleAction.Drop
        });

        var msgFromApp1 = new GotifyMessage { Id = 301, AppId = 1, ApplicationName = "App1", Title = "Test Alert" };
        var msgFromApp2 = new GotifyMessage { Id = 302, AppId = 2, ApplicationName = "App2", Title = "Test Alert" };

        Assert.Equal(FilterDecision.Drop, engine.Evaluate(msgFromApp1, config, isDndActive: false).Decision);
        Assert.Equal(FilterDecision.Drop, engine.Evaluate(msgFromApp2, config, isDndActive: false).Decision);
    }

    [Fact]
    public void ApplyRecommendation_SetsAppFilterIdAndName()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        var rec = new RuleRecommendation
        {
            AppId = 99,
            AppName = "Plex",
            Title = "Mute Plex transcode warnings",
            SuggestedField = RuleField.Title,
            SuggestedOperator = RuleOperator.Contains,
            SuggestedPattern = "Transcode Slow",
            SuggestedAction = RuleAction.SilentLog,
            MatchCount = 6
        };

        rulesVm.ApplyRecommendation(rec, RuleAction.SilentLog);

        Assert.Single(rulesVm.Rules);
        var rule = rulesVm.Rules[0];
        Assert.Equal(99, rule.AppFilterId);
        Assert.Equal("Plex", rule.AppFilterName);
        Assert.Equal("Plex", rule.ScopeDescription);
    }

    [Fact]
    public void RulesViewModel_ConfirmImport_OnlyImportsSelectedCandidates()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        var rule1 = new NotificationRule { Name = "Rule 1", Pattern = "P1", TargetField = RuleField.Title };
        var rule2 = new NotificationRule { Name = "Rule 2", Pattern = "P2", TargetField = RuleField.Title };

        rulesVm.ImportCandidates.Add(new RuleImportCandidate(rule1, isDuplicate: false) { IsSelected = true });
        rulesVm.ImportCandidates.Add(new RuleImportCandidate(rule2, isDuplicate: false) { IsSelected = false });
        rulesVm.IsImportModalOpen = true;

        Assert.Equal(1, rulesVm.SelectedImportCount);
        Assert.Equal(2, rulesVm.TotalImportCount);
        Assert.Equal("1 of 2 selected", rulesVm.ImportSummaryText);

        rulesVm.ConfirmImport();

        Assert.False(rulesVm.IsImportModalOpen);
        Assert.Single(rulesVm.Rules);
        Assert.Equal("Rule 1", rulesVm.Rules[0].Name);
        Assert.Single(configService.Config.Rules);
        Assert.Contains("Successfully imported 1 rule", rulesVm.RuleStatusFeedback);
    }

    [Fact]
    public void RulesViewModel_SelectAllImport_TogglesAllCandidates()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        var rule1 = new NotificationRule { Name = "Rule 1", Pattern = "P1" };
        var rule2 = new NotificationRule { Name = "Rule 2", Pattern = "P2" };

        rulesVm.ImportCandidates.Add(new RuleImportCandidate(rule1, false) { IsSelected = true });
        rulesVm.ImportCandidates.Add(new RuleImportCandidate(rule2, false) { IsSelected = true });

        // Deselect all
        rulesVm.SelectAllImport(false);
        Assert.Equal(0, rulesVm.SelectedImportCount);
        Assert.False(rulesVm.ImportCandidates[0].IsSelected);
        Assert.False(rulesVm.ImportCandidates[1].IsSelected);

        // Select all
        rulesVm.SelectAllImport(true);
        Assert.Equal(2, rulesVm.SelectedImportCount);
        Assert.True(rulesVm.ImportCandidates[0].IsSelected);
        Assert.True(rulesVm.ImportCandidates[1].IsSelected);
    }

    [Fact]
    public void SettingsViewModel_FullSettingsSerialization_PreservesAppFiltersAndMinPriority()
    {
        var configService = new DummyConfigService();
        configService.Config.ServerUrl = "https://gotify.test.lan";
        configService.Config.ClientToken = "token_abc123";
        configService.Config.GlobalMinPriority = 4;
        configService.Config.AppFilters[5] = new AppFilterSetting
        {
            AppId = 5,
            AppName = "Sonarr",
            Enabled = true,
            MinPriority = 6
        };
        configService.Config.AppFilters[9] = new AppFilterSetting
        {
            AppId = 9,
            AppName = "Profilarr",
            Enabled = false,
            MinPriority = 8
        };

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(configService.Config, options);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<GotifyClientConfig>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal("https://gotify.test.lan", deserialized.ServerUrl);
        Assert.Equal("token_abc123", deserialized.ClientToken);
        Assert.Equal(4, deserialized.GlobalMinPriority);
        Assert.Equal(2, deserialized.AppFilters.Count);
        Assert.Equal(6, deserialized.AppFilters[5].MinPriority);
        Assert.True(deserialized.AppFilters[5].Enabled);
        Assert.Equal(8, deserialized.AppFilters[9].MinPriority);
        Assert.False(deserialized.AppFilters[9].Enabled);
    }

    [Fact]
    public void EditRule_PopulatesBuilderAndUpdatesExistingRuleInPlace()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        var existingRule = new NotificationRule
        {
            Id = "rule_original_1",
            Name = "Mute Sonarr Backups",
            Enabled = true,
            AppFilterId = 2,
            AppFilterName = "Sonarr",
            TargetField = RuleField.Title,
            Operator = RuleOperator.Contains,
            Pattern = "Backup Finished",
            Action = RuleAction.SilentLog
        };
        configService.Config.Rules.Add(existingRule);
        rulesVm.Rules.Add(existingRule);

        // 1. Trigger Edit
        rulesVm.EditRuleCommand.Execute(existingRule);

        Assert.True(rulesVm.IsEditingRule);
        Assert.Same(existingRule, rulesVm.EditingRule);
        Assert.Equal("Mute Sonarr Backups", rulesVm.NewRuleName);
        Assert.Equal("Backup Finished", rulesVm.NewRulePattern);
        Assert.Equal("✓ Update Rule", rulesVm.RuleFormSubmitButtonText);
        Assert.Contains("Edit Rule", rulesVm.RuleFormHeaderTitle);

        // 2. Modify properties in builder
        rulesVm.NewRuleName = "Drop Sonarr Backups & Tests";
        rulesVm.NewRulePattern = "Backup or Test";
        rulesVm.NewRuleAction = RuleAction.Drop;

        // 3. Submit Update
        rulesVm.AddRuleCommand.Execute(null);

        // Form should reset and exit edit mode
        Assert.False(rulesVm.IsEditingRule);
        Assert.Null(rulesVm.EditingRule);
        Assert.Equal("+ Add Custom Rule", rulesVm.RuleFormSubmitButtonText);

        // Original rule should be updated in-place without duplicating
        Assert.Single(rulesVm.Rules);
        Assert.Same(existingRule, rulesVm.Rules[0]);
        Assert.Equal("Drop Sonarr Backups & Tests", existingRule.Name);
        Assert.Equal("Backup or Test", existingRule.Pattern);
        Assert.Equal(RuleAction.Drop, existingRule.Action);
    }

    [Fact]
    public void CancelEdit_RestoresBuilderWithoutMutatingOriginalRule()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        var existingRule = new NotificationRule
        {
            Id = "rule_keep_1",
            Name = "Original Rule",
            Pattern = "KeepThis",
            Action = RuleAction.SilentLog
        };
        rulesVm.Rules.Add(existingRule);

        // Start editing
        rulesVm.StartEditingRule(existingRule);
        Assert.True(rulesVm.IsEditingRule);

        // Change values in form
        rulesVm.NewRuleName = "Unsaved Changes";
        rulesVm.NewRulePattern = "DiscardThis";

        // Cancel
        rulesVm.CancelEditCommand.Execute(null);

        Assert.False(rulesVm.IsEditingRule);
        Assert.Null(rulesVm.EditingRule);
        Assert.Empty(rulesVm.NewRuleName);
        Assert.Empty(rulesVm.NewRulePattern);
        Assert.Equal("Original Rule", existingRule.Name);
        Assert.Equal("KeepThis", existingRule.Pattern);
    }

    [Fact]
    public void RuleImportCandidate_CriteriaSummary_MatchesFormat()
    {
        var rule = new NotificationRule
        {
            Name = "Import Test",
            TargetField = RuleField.Title,
            Operator = RuleOperator.Contains,
            Pattern = "Download Finished"
        };
        var candidate = new RuleImportCandidate(rule, isDuplicate: false);

        Assert.Equal("Title Contains: 'Download Finished'", candidate.CriteriaSummary);
        Assert.True(candidate.IsSelected);
    }

    [Fact]
    public void ExportAndImport_Roundtrip_WorksWithoutCrashing()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        var rule = new NotificationRule
        {
            Id = "roundtrip_1",
            Name = "Roundtrip Rule",
            AppFilterId = 5,
            AppFilterName = "Prowlarr",
            TargetField = RuleField.Message,
            Operator = RuleOperator.Contains,
            Pattern = "Indexer Unhealthy",
            Action = RuleAction.UrgentAlert,
            CaseSensitive = true,
            Enabled = true
        };
        rulesVm.Rules.Add(rule);

        // Serialize export wrapper
        var export = new RulesExportWrapper
        {
            Version = 1,
            ExportDateUtc = DateTime.UtcNow,
            Rules = rulesVm.Rules.ToList()
        };

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(export, options);

        // Deserialize as wrapped
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<RulesExportWrapper>(json, options);
        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Rules);

        // Populate candidates as done in ImportRules
        rulesVm.ImportCandidates.Clear();
        foreach (var r in deserialized.Rules)
        {
            var candidate = new RuleImportCandidate(r, isDuplicate: false);
            rulesVm.ImportCandidates.Add(candidate);
        }

        Assert.Single(rulesVm.ImportCandidates);
        Assert.Equal("Message Contains: 'Indexer Unhealthy'", rulesVm.ImportCandidates[0].CriteriaSummary);
        Assert.Equal("Prowlarr", rulesVm.ImportCandidates[0].ScopeDescription);
    }

    [Fact]
    public void AppItemViewModel_StatusAndThresholdFormatting()
    {
        var setting = new AppFilterSetting
        {
            AppId = 1,
            AppName = "Prowlarr",
            Enabled = true,
            MinPriority = 4
        };

        var appVm = new AppItemViewModel(setting, () => { });

        Assert.True(appVm.IsEnabled);
        Assert.Equal("Alerts Active", appVm.StatusLabel);
        Assert.Equal("#10B981", appVm.StatusColor);
        Assert.Equal("\u2265 4 (Normal+)", appVm.MinPriorityText);
        Assert.Contains("below priority 4 will be dropped", appVm.MinPriorityDescription);

        // Toggle disabled
        appVm.IsEnabled = false;
        Assert.False(appVm.IsEnabled);
        Assert.Equal("Alerts Muted", appVm.StatusLabel);
        Assert.Equal("#94A3B8", appVm.StatusColor);

        // Test threshold scale
        appVm.MinPriority = 0;
        Assert.Equal("All (0+)", appVm.MinPriorityText);
        Assert.Contains("All notifications from this application", appVm.MinPriorityDescription);

        appVm.MinPriority = 8;
        Assert.Equal("\u2265 8 (Urgent)", appVm.MinPriorityText);
    }

    [Fact]
    public void SettingsViewModel_CollapsibleSectionsAndSummaries()
    {
        var configService = new DummyConfigService();
        var vm = new SettingsViewModel(configService, null!, null!, null!);

        // Verify default states
        Assert.True(vm.IsConnectionExpanded);
        Assert.True(vm.IsNotificationsExpanded);
        Assert.False(vm.IsFilteringExpanded);
        Assert.False(vm.IsDndExpanded);

        // Toggle collapsible sections
        vm.ToggleConnectionCommand.Execute(null);
        Assert.False(vm.IsConnectionExpanded);

        vm.ToggleFilteringCommand.Execute(null);
        Assert.True(vm.IsFilteringExpanded);

        // Verify summary text properties
        Assert.Contains("Floating Banner", vm.NotificationsSummaryText);
        Assert.Contains("Debounce", vm.FilteringSummaryText);
        Assert.Contains("Export / Import", vm.BackupSummaryText);
    }

    [Fact]
    public void RulesViewModel_TransientFeedbackAndCollapseClearing()
    {
        var configService = new DummyConfigService();
        var rulesVm = new RulesViewModel(configService);

        rulesVm.SetTransientFeedback("✓ Test feedback message");
        Assert.True(rulesVm.HasRuleStatusFeedback);
        Assert.Equal("✓ Test feedback message", rulesVm.RuleStatusFeedback);

        // Collapsing configured rules immediately clears feedback
        rulesVm.IsConfiguredRulesExpanded = true;
        rulesVm.ToggleConfiguredRulesCommand.Execute(null);
        Assert.False(rulesVm.IsConfiguredRulesExpanded);
        Assert.False(rulesVm.HasRuleStatusFeedback);
        Assert.Equal(string.Empty, rulesVm.RuleStatusFeedback);
    }
}

using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using Microsoft.Win32;

namespace GotifyWin.ViewModels;

public class RulesExportWrapper
{
    public int Version { get; set; } = 1;
    public DateTime ExportDateUtc { get; set; } = DateTime.UtcNow;
    public List<NotificationRule> Rules { get; set; } = new();
}

public class RuleImportCandidate : ViewModelBase
{
    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public NotificationRule Rule { get; }
    public bool IsDuplicate { get; }

    public string Name => Rule.Name;
    public string ScopeDescription => Rule.ScopeDescription;
    public string TargetField => Rule.TargetField.ToString();
    public string Operator => Rule.Operator.ToString();
    public string Pattern => Rule.Pattern;
    public RuleAction Action => Rule.Action;
    public string ActionDisplayName => Rule.ActionDisplayName;
    public string CriteriaSummary => $"{TargetField} {Operator}: '{Pattern}'";

    public RuleImportCandidate(NotificationRule rule, bool isDuplicate)
    {
        Rule = rule;
        IsDuplicate = isDuplicate;
    }
}

public class AppFilterOption
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;

    public override bool Equals(object? obj) =>
        obj is AppFilterOption other && Id == other.Id && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(Id, Name?.ToLowerInvariant());
}

public class RecommendationItemViewModel : ViewModelBase
{
    private readonly RuleRecommendation _recommendation;
    private readonly Action<RuleRecommendation, RuleAction> _onApply;
    private readonly Action<RuleRecommendation> _onDismiss;
    private bool _isPreviewExpanded;

    public RuleRecommendation Recommendation => _recommendation;
    public string Id => _recommendation.Id;
    public long AppId => _recommendation.AppId;
    public string AppName => _recommendation.AppName;
    public string Title => _recommendation.Title;
    public string Description => _recommendation.Description;
    public string SuggestedPattern => _recommendation.SuggestedPattern;
    public RuleField SuggestedField => _recommendation.SuggestedField;
    public RuleOperator SuggestedOperator => _recommendation.SuggestedOperator;
    public int MatchCount => _recommendation.MatchCount;
    public List<GotifyMessage> SampleMessages => _recommendation.SampleMessages;

    public bool IsPreviewExpanded
    {
        get => _isPreviewExpanded;
        set
        {
            if (SetProperty(ref _isPreviewExpanded, value))
            {
                OnPropertyChanged(nameof(PreviewToggleText));
            }
        }
    }

    public string PreviewToggleText => IsPreviewExpanded
        ? $"▲ Hide Preview ({MatchCount})"
        : $"▼ Preview {MatchCount} Messages";

    public ICommand TogglePreviewCommand { get; }
    public ICommand ApplySilentLogCommand { get; }
    public ICommand ApplyDropCommand { get; }
    public ICommand DismissCommand { get; }

    public RecommendationItemViewModel(
        RuleRecommendation recommendation,
        Action<RuleRecommendation, RuleAction> onApply,
        Action<RuleRecommendation> onDismiss)
    {
        _recommendation = recommendation;
        _onApply = onApply;
        _onDismiss = onDismiss;

        TogglePreviewCommand = new RelayCommand(() => IsPreviewExpanded = !IsPreviewExpanded);
        ApplySilentLogCommand = new RelayCommand(() => _onApply(_recommendation, RuleAction.SilentLog));
        ApplyDropCommand = new RelayCommand(() => _onApply(_recommendation, RuleAction.Drop));
        DismissCommand = new RelayCommand(() => _onDismiss(_recommendation));
    }
}

public class RulesViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly INotificationPatternAnalyzer? _patternAnalyzer;
    private readonly Func<IEnumerable<GotifyMessage>>? _messageSource;
    private readonly Func<Task<IEnumerable<GotifyMessage>>>? _historyFetcher;

    private readonly ObservableCollection<NotificationRule> _rules = new();
    private readonly ObservableCollection<RecommendationItemViewModel> _recommendations = new();

    private bool _isReanalyzing;
    public bool IsReanalyzing
    {
        get => _isReanalyzing;
        set
        {
            if (SetProperty(ref _isReanalyzing, value))
            {
                OnPropertyChanged(nameof(CanReanalyze));
            }
        }
    }

    public bool CanReanalyze => !IsReanalyzing;

    private string _reanalyzeButtonText = "⚡ Re-scan History";
    public string ReanalyzeButtonText
    {
        get => _reanalyzeButtonText;
        set => SetProperty(ref _reanalyzeButtonText, value);
    }

    private bool _isRecommendationsExpanded = true;
    public bool IsRecommendationsExpanded
    {
        get => _isRecommendationsExpanded;
        set => SetProperty(ref _isRecommendationsExpanded, value);
    }

    private bool _isConfiguredRulesExpanded = true;
    public bool IsConfiguredRulesExpanded
    {
        get => _isConfiguredRulesExpanded;
        set => SetProperty(ref _isConfiguredRulesExpanded, value);
    }

    private bool _isAdvancedExpanded;
    public bool IsAdvancedExpanded
    {
        get => _isAdvancedExpanded;
        set => SetProperty(ref _isAdvancedExpanded, value);
    }

    private NotificationRule? _editingRule;
    public NotificationRule? EditingRule
    {
        get => _editingRule;
        set
        {
            if (SetProperty(ref _editingRule, value))
            {
                OnPropertyChanged(nameof(IsEditingRule));
                OnPropertyChanged(nameof(RuleFormHeaderTitle));
                OnPropertyChanged(nameof(RuleFormHeaderSubtitle));
                OnPropertyChanged(nameof(RuleFormSubmitButtonText));
            }
        }
    }

    public bool IsEditingRule => EditingRule != null;
    public string RuleFormHeaderTitle => IsEditingRule ? $"✏️ Edit Rule: \"{EditingRule?.Name}\"" : "⚙️ Manual Rule Builder";
    public string RuleFormHeaderSubtitle => IsEditingRule
        ? "Modify rule scope, pattern, or action. Click 'Update Rule' to apply your changes or 'Cancel' to keep the original."
        : "Create manual rules using custom regex, substrings, and target fields.";
    public string RuleFormSubmitButtonText => IsEditingRule ? "✓ Update Rule" : "+ Add Custom Rule";

    private string _newRuleName = string.Empty;
    public string NewRuleName
    {
        get => _newRuleName;
        set => SetProperty(ref _newRuleName, value);
    }

    public ObservableCollection<AppFilterOption> AvailableAppFilterOptions { get; } = new();

    private AppFilterOption? _selectedAppFilterOption;
    public AppFilterOption? SelectedAppFilterOption
    {
        get => _selectedAppFilterOption;
        set => SetProperty(ref _selectedAppFilterOption, value);
    }

    private RuleField _newRuleField = RuleField.Title;
    public RuleField NewRuleField
    {
        get => _newRuleField;
        set => SetProperty(ref _newRuleField, value);
    }

    private RuleOperator _newRuleOperator = RuleOperator.Contains;
    public RuleOperator NewRuleOperator
    {
        get => _newRuleOperator;
        set => SetProperty(ref _newRuleOperator, value);
    }

    private string _newRulePattern = string.Empty;
    public string NewRulePattern
    {
        get => _newRulePattern;
        set => SetProperty(ref _newRulePattern, value);
    }

    private RuleAction _newRuleAction = RuleAction.SilentLog;
    public RuleAction NewRuleAction
    {
        get => _newRuleAction;
        set => SetProperty(ref _newRuleAction, value);
    }

    private bool _newRuleCaseSensitive;
    public bool NewRuleCaseSensitive
    {
        get => _newRuleCaseSensitive;
        set => SetProperty(ref _newRuleCaseSensitive, value);
    }

    public ObservableCollection<NotificationRule> Rules => _rules;
    public ObservableCollection<RecommendationItemViewModel> Recommendations => _recommendations;
    public bool HasRecommendations => _recommendations.Count > 0;

    public ObservableCollection<RuleImportCandidate> ImportCandidates { get; } = new();

    private bool _isImportModalOpen;
    public bool IsImportModalOpen
    {
        get => _isImportModalOpen;
        set => SetProperty(ref _isImportModalOpen, value);
    }

    public int SelectedImportCount => ImportCandidates.Count(c => c.IsSelected);
    public int TotalImportCount => ImportCandidates.Count;
    public string ImportSummaryText => $"{SelectedImportCount} of {TotalImportCount} selected";
    public string ImportButtonText => SelectedImportCount > 0 ? $"Import Selected ({SelectedImportCount})" : "Import Selected (0)";

    private CancellationTokenSource? _feedbackCts;

    public void SetTransientFeedback(string message, int durationMs = 4000)
    {
        _feedbackCts?.Cancel();
        _feedbackCts = new CancellationTokenSource();
        var token = _feedbackCts.Token;

        RuleStatusFeedback = message;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(durationMs, token);
                if (!token.IsCancellationRequested)
                {
                    UiDispatcher.RunOnUI(() =>
                    {
                        if (!token.IsCancellationRequested && RuleStatusFeedback == message)
                        {
                            RuleStatusFeedback = string.Empty;
                        }
                    });
                }
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    private string _ruleStatusFeedback = string.Empty;
    public string RuleStatusFeedback
    {
        get => _ruleStatusFeedback;
        set
        {
            if (SetProperty(ref _ruleStatusFeedback, value))
            {
                OnPropertyChanged(nameof(HasRuleStatusFeedback));
            }
        }
    }
    public bool HasRuleStatusFeedback => !string.IsNullOrEmpty(RuleStatusFeedback);

    public int RecommendationsCount => _recommendations.Count;
    public string RecommendationsSummaryText => $"{RecommendationsCount} suggestion{(RecommendationsCount == 1 ? "" : "s")} ready";

    public int ActiveRulesCount => _rules.Count(r => r.Enabled);
    public int PausedRulesCount => _rules.Count(r => !r.Enabled);
    public int TotalRulesCount => _rules.Count;
    public bool HasActiveRules => _rules.Count > 0;
    public string RulesSummaryText => _rules.Count == 0 ? "0 rules" : PausedRulesCount == 0 ? $"{ActiveRulesCount} active" : $"{ActiveRulesCount} active • {PausedRulesCount} paused";

    public ICommand AddRuleCommand { get; }
    public ICommand EditRuleCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand ToggleRuleCommand { get; }
    public ICommand ToggleAdvancedCommand { get; }
    public ICommand ToggleRecommendationsCommand { get; }
    public ICommand ToggleConfiguredRulesCommand { get; }
    public ICommand ReanalyzeCommand { get; }
    public ICommand ExportRulesCommand { get; }
    public ICommand ImportRulesCommand { get; }
    public ICommand ConfirmImportCommand { get; }
    public ICommand CancelImportCommand { get; }
    public ICommand SelectAllImportCommand { get; }

    public RulesViewModel(
        IConfigService configService,
        INotificationPatternAnalyzer? patternAnalyzer = null,
        Func<IEnumerable<GotifyMessage>>? messageSource = null,
        Func<Task<IEnumerable<GotifyMessage>>>? historyFetcher = null)
    {
        _configService = configService;
        _patternAnalyzer = patternAnalyzer;
        _messageSource = messageSource;
        _historyFetcher = historyFetcher;

        AddRuleCommand = new RelayCommand(AddRule);
        EditRuleCommand = new RelayCommand<NotificationRule>(StartEditingRule);
        CancelEditCommand = new RelayCommand(CancelEditingRule);
        DeleteRuleCommand = new RelayCommand<NotificationRule>(DeleteRule);
        ToggleRuleCommand = new RelayCommand<NotificationRule>(ToggleRule);
        ToggleAdvancedCommand = new RelayCommand(() => IsAdvancedExpanded = !IsAdvancedExpanded);
        ToggleRecommendationsCommand = new RelayCommand(() => IsRecommendationsExpanded = !IsRecommendationsExpanded);
        ToggleConfiguredRulesCommand = new RelayCommand(() =>
        {
            IsConfiguredRulesExpanded = !IsConfiguredRulesExpanded;
            if (!IsConfiguredRulesExpanded)
            {
                _feedbackCts?.Cancel();
                RuleStatusFeedback = string.Empty;
            }
        });
        ReanalyzeCommand = new RelayCommand(async () => await ReanalyzeHistoryAsync());
        ExportRulesCommand = new RelayCommand(ExportRules);
        ImportRulesCommand = new RelayCommand(ImportRules);
        ConfirmImportCommand = new RelayCommand(ConfirmImport);
        CancelImportCommand = new RelayCommand(CancelImport);
        SelectAllImportCommand = new RelayCommand<object>(p => SelectAllImport(p?.ToString() == "True"));

        RefreshAvailableApps();
        LoadRules();
    }

    public void RefreshAvailableApps()
    {
        var currentSelectedId = SelectedAppFilterOption?.Id ?? 0;
        var list = new List<AppFilterOption>
        {
            new() { Id = 0, Name = "All Applications" }
        };

        if (_configService.Config.AppFilters != null)
        {
            foreach (var app in _configService.Config.AppFilters.Values.OrderBy(a => a.AppName))
            {
                if (app.AppId > 0 && !string.IsNullOrWhiteSpace(app.AppName))
                {
                    if (!list.Any(x => x.Id == app.AppId))
                    {
                        list.Add(new AppFilterOption { Id = app.AppId, Name = app.AppName });
                    }
                }
            }
        }

        if (_messageSource != null)
        {
            try
            {
                var msgs = _messageSource();
                foreach (var m in msgs)
                {
                    if (m.AppId > 0 && !string.IsNullOrWhiteSpace(m.DisplayAppName))
                    {
                        if (!list.Any(x => x.Id == m.AppId))
                        {
                            list.Add(new AppFilterOption { Id = m.AppId, Name = m.DisplayAppName });
                        }
                    }
                }
            }
            catch { }
        }

        AvailableAppFilterOptions.Clear();
        foreach (var opt in list)
        {
            AvailableAppFilterOptions.Add(opt);
        }

        SelectedAppFilterOption = AvailableAppFilterOptions.FirstOrDefault(x => x.Id == currentSelectedId)
            ?? AvailableAppFilterOptions.FirstOrDefault();
    }

    public void PrepareManualRuleFromNotification(GotifyMessage message)
    {
        PrepopulateFromMessage(message);
        IsRecommendationsExpanded = false;
        IsConfiguredRulesExpanded = false;
        IsAdvancedExpanded = true;
    }

    public void PrepopulateFromMessage(GotifyMessage message)
    {
        RefreshAvailableApps();

        if (message.AppId > 0)
        {
            var matching = AvailableAppFilterOptions.FirstOrDefault(x => x.Id == message.AppId);
            if (matching == null)
            {
                matching = new AppFilterOption { Id = message.AppId, Name = message.DisplayAppName };
                AvailableAppFilterOptions.Add(matching);
            }
            SelectedAppFilterOption = matching;
        }
        else
        {
            SelectedAppFilterOption = AvailableAppFilterOptions.FirstOrDefault();
        }

        var pattern = !string.IsNullOrWhiteSpace(message.Title)
            ? message.Title
            : message.Message;

        var shortPattern = pattern.Length > 28 ? pattern[..28] + "..." : pattern;
        NewRuleName = $"Filter {message.DisplayAppName}: {shortPattern}";
        NewRuleField = !string.IsNullOrWhiteSpace(message.Title) ? RuleField.Title : RuleField.Message;
        NewRuleOperator = RuleOperator.Contains;
        NewRulePattern = pattern;
        NewRuleAction = RuleAction.SilentLog;
        NewRuleCaseSensitive = false;
        IsAdvancedExpanded = true;
    }

    public void SetRecommendations(IEnumerable<RuleRecommendation> recommendations)
    {
        _recommendations.Clear();
        foreach (var rec in recommendations)
        {
            _recommendations.Add(new RecommendationItemViewModel(rec, ApplyRecommendation, DismissRecommendation));
        }
        NotifyRecommendationsCountChanged();
    }

    public async Task ReanalyzeHistoryAsync()
    {
        if (_patternAnalyzer == null || IsReanalyzing) return;

        IsReanalyzing = true;
        ReanalyzeButtonText = "⏳ Scanning...";

        try
        {
            // Brief visual pause so the user perceives the scanning feedback state
            await Task.Delay(400);

            IEnumerable<GotifyMessage> messages = _messageSource != null ? _messageSource().ToList() : Enumerable.Empty<GotifyMessage>();

            // If local feed has fewer than 3 messages (e.g. cleared by user), try to fetch from server if available
            if (messages.Count() < 3 && _historyFetcher != null)
            {
                try
                {
                    var fetched = await _historyFetcher();
                    if (fetched != null && fetched.Any())
                    {
                        messages = fetched.ToList();
                    }
                }
                catch { }
            }

            // If still fewer than 3 messages, RETAIN current recommendations so they are not wiped out
            if (messages.Count() < 3)
            {
                ReanalyzeButtonText = "✓ Up to date";
                await Task.Delay(1400);
                return;
            }

            var recs = _patternAnalyzer.Analyze(messages, _configService.Config, threshold: 3);
            if (recs.Count > 0 || messages.Count() >= 3)
            {
                SetRecommendations(recs);
                ReanalyzeButtonText = recs.Count > 0 ? $"✓ Found {recs.Count}" : "✓ Up to date";
            }
            else
            {
                ReanalyzeButtonText = "✓ Up to date";
            }

            await Task.Delay(1400);
        }
        finally
        {
            ReanalyzeButtonText = "⚡ Re-scan History";
            IsReanalyzing = false;
        }
    }

    public void ApplyRecommendation(RuleRecommendation recommendation, RuleAction action)
    {
        var ruleName = string.IsNullOrWhiteSpace(recommendation.Title)
            ? $"{recommendation.SuggestedField} {recommendation.SuggestedOperator} '{recommendation.SuggestedPattern}'"
            : recommendation.Title;

        var rule = new NotificationRule
        {
            Name = ruleName,
            Enabled = true,
            AppFilterId = recommendation.AppId > 0 ? recommendation.AppId : null,
            AppFilterName = !string.IsNullOrWhiteSpace(recommendation.AppName) ? recommendation.AppName : null,
            TargetField = recommendation.SuggestedField,
            Operator = recommendation.SuggestedOperator,
            Pattern = recommendation.SuggestedPattern,
            Action = action,
            CaseSensitive = false
        };

        AttachRuleListener(rule);
        _rules.Add(rule);
        _configService.Config.Rules.Add(rule);
        _configService.Save();
        NotifyRulesCountChanged();

        // Remove from recommendations
        var match = _recommendations.FirstOrDefault(r => r.Id == recommendation.Id);
        if (match != null)
        {
            _recommendations.Remove(match);
            NotifyRecommendationsCountChanged();
        }
    }

    public void DismissRecommendation(RuleRecommendation recommendation)
    {
        var match = _recommendations.FirstOrDefault(r => r.Id == recommendation.Id);
        if (match != null)
        {
            _recommendations.Remove(match);
            NotifyRecommendationsCountChanged();
        }
    }

    private void LoadRules()
    {
        _rules.Clear();
        foreach (var rule in _configService.Config.Rules)
        {
            AttachRuleListener(rule);
            _rules.Add(rule);
        }
        NotifyRulesCountChanged();
    }

    private void AttachRuleListener(NotificationRule rule)
    {
        rule.PropertyChanged += (s, e) =>
        {
            if (e?.PropertyName == nameof(NotificationRule.Enabled))
            {
                _configService.Save();
                NotifyRulesCountChanged();
            }
        };
    }

    public void StartEditingRule(NotificationRule? rule)
    {
        if (rule == null) return;
        RefreshAvailableApps();
        EditingRule = rule;

        NewRuleName = rule.Name;
        NewRuleField = rule.TargetField;
        NewRuleOperator = rule.Operator;
        NewRulePattern = rule.Pattern;
        NewRuleAction = rule.Action;
        NewRuleCaseSensitive = rule.CaseSensitive;

        if (rule.AppFilterId.HasValue && rule.AppFilterId.Value > 0)
        {
            var match = AvailableAppFilterOptions.FirstOrDefault(x => x.Id == rule.AppFilterId.Value);
            if (match == null)
            {
                match = new AppFilterOption { Id = rule.AppFilterId.Value, Name = rule.AppFilterName ?? $"App #{rule.AppFilterId}" };
                AvailableAppFilterOptions.Add(match);
            }
            SelectedAppFilterOption = match;
        }
        else
        {
            SelectedAppFilterOption = AvailableAppFilterOptions.FirstOrDefault();
        }

        IsAdvancedExpanded = true;
        RuleStatusFeedback = $"✏️ Editing rule '{rule.Name}'. Revise criteria below and click 'Update Rule'.";
    }

    public void CancelEditingRule()
    {
        EditingRule = null;
        NewRuleName = string.Empty;
        NewRulePattern = string.Empty;
        NewRuleField = RuleField.Title;
        NewRuleOperator = RuleOperator.Contains;
        NewRuleAction = RuleAction.SilentLog;
        NewRuleCaseSensitive = false;
        SelectedAppFilterOption = AvailableAppFilterOptions.FirstOrDefault();
        _feedbackCts?.Cancel();
        RuleStatusFeedback = string.Empty;
    }

    private void AddRule()
    {
        if (string.IsNullOrWhiteSpace(NewRulePattern))
            return;

        var name = string.IsNullOrWhiteSpace(NewRuleName)
            ? $"{NewRuleField} {NewRuleOperator} '{NewRulePattern}'"
            : NewRuleName.Trim();

        var appId = (SelectedAppFilterOption != null && SelectedAppFilterOption.Id > 0)
            ? SelectedAppFilterOption.Id
            : (long?)null;
        var appName = (SelectedAppFilterOption != null && SelectedAppFilterOption.Id > 0)
            ? SelectedAppFilterOption.Name
            : null;

        if (IsEditingRule && EditingRule != null)
        {
            // In-place update of existing rule
            EditingRule.Name = name;
            EditingRule.AppFilterId = appId;
            EditingRule.AppFilterName = appName;
            EditingRule.TargetField = NewRuleField;
            EditingRule.Operator = NewRuleOperator;
            EditingRule.Pattern = NewRulePattern.Trim();
            EditingRule.Action = NewRuleAction;
            EditingRule.CaseSensitive = NewRuleCaseSensitive;
            EditingRule.NotifyAllPropertiesChanged();

            _configService.Save();

            // Refresh collection for UI
            var index = _rules.IndexOf(EditingRule);
            if (index >= 0)
            {
                _rules[index] = EditingRule;
            }

            var updatedName = EditingRule.Name;
            CancelEditingRule();
            SetTransientFeedback($"✓ Successfully updated rule '{updatedName}'.");
            NotifyRulesCountChanged();
            return;
        }

        var rule = new NotificationRule
        {
            Name = name,
            Enabled = true,
            AppFilterId = appId,
            AppFilterName = appName,
            TargetField = NewRuleField,
            Operator = NewRuleOperator,
            Pattern = NewRulePattern.Trim(),
            Action = NewRuleAction,
            CaseSensitive = NewRuleCaseSensitive
        };

        AttachRuleListener(rule);
        _rules.Add(rule);
        _configService.Config.Rules.Add(rule);
        _configService.Save();
        NotifyRulesCountChanged();

        // Reset inputs
        NewRuleName = string.Empty;
        NewRulePattern = string.Empty;
        SelectedAppFilterOption = AvailableAppFilterOptions.FirstOrDefault();
        SetTransientFeedback($"✓ Created rule '{rule.Name}'.");
    }

    private void DeleteRule(NotificationRule? rule)
    {
        if (rule == null) return;
        if (rule == EditingRule)
        {
            CancelEditingRule();
        }
        _rules.Remove(rule);
        _configService.Config.Rules.Remove(rule);
        _configService.Save();
        NotifyRulesCountChanged();
    }

    private void ToggleRule(NotificationRule? rule)
    {
        if (rule == null) return;
        rule.Enabled = !rule.Enabled;
        _configService.Save();
        NotifyRulesCountChanged();
        OnPropertyChanged(nameof(Rules));
    }

    private void NotifyRulesCountChanged()
    {
        OnPropertyChanged(nameof(ActiveRulesCount));
        OnPropertyChanged(nameof(PausedRulesCount));
        OnPropertyChanged(nameof(TotalRulesCount));
        OnPropertyChanged(nameof(RulesSummaryText));
        OnPropertyChanged(nameof(HasActiveRules));
    }

    private void NotifyRecommendationsCountChanged()
    {
        OnPropertyChanged(nameof(RecommendationsCount));
        OnPropertyChanged(nameof(RecommendationsSummaryText));
        OnPropertyChanged(nameof(HasRecommendations));
    }

    public void ExportRules()
    {
        if (_rules.Count == 0)
        {
            SetTransientFeedback("⚠️ No configured rules to export.");
            return;
        }

        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Filter Rules",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"gotify-rules-{DateTime.Now:yyyyMMdd-HHmm}.json"
            };

            if (dlg.ShowDialog() == true)
            {
                var export = new RulesExportWrapper
                {
                    Version = 1,
                    ExportDateUtc = DateTime.UtcNow,
                    Rules = _rules.ToList()
                };

                var json = JsonSerializer.Serialize(export, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                });

                File.WriteAllText(dlg.FileName, json);
                SetTransientFeedback($"✓ Exported {_rules.Count} rules to {Path.GetFileName(dlg.FileName)}");
            }
        }
        catch (Exception ex)
        {
            SetTransientFeedback($"Export error: {ex.Message}");
        }
    }

    public void ImportRules()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Filter Rules",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                ImportValidator.EnsureFileSizeAllowed(dlg.FileName);
                var json = File.ReadAllText(dlg.FileName);
                List<NotificationRule>? incomingRules = null;

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() }
                };

                // Try wrapped format first
                try
                {
                    var wrapped = JsonSerializer.Deserialize<RulesExportWrapper>(json, options);
                    if (wrapped?.Rules != null && wrapped.Rules.Count > 0)
                    {
                        incomingRules = wrapped.Rules;
                    }
                }
                catch { }

                // Fallback to direct array
                if (incomingRules == null)
                {
                    try
                    {
                        incomingRules = JsonSerializer.Deserialize<List<NotificationRule>>(json, options);
                    }
                    catch { }
                }

                // SEC: Drop malformed/oversized/invalid-regex rules and cap the total count
                var skippedRules = 0;
                if (incomingRules != null)
                {
                    incomingRules = ImportValidator.SanitizeRules(incomingRules, out skippedRules);
                }

                if (incomingRules == null || incomingRules.Count == 0)
                {
                    SetTransientFeedback(skippedRules > 0
                        ? $"⚠️ No valid rules found ({skippedRules} skipped as invalid)."
                        : "⚠️ No valid filter rules found in selected file.");
                    return;
                }

                ImportCandidates.Clear();
                foreach (var rule in incomingRules)
                {
                    var isDup = _rules.Any(r =>
                        r.TargetField == rule.TargetField &&
                        r.Operator == rule.Operator &&
                        string.Equals(r.Pattern, rule.Pattern, StringComparison.OrdinalIgnoreCase) &&
                        r.AppFilterId == rule.AppFilterId);

                    var candidate = new RuleImportCandidate(rule, isDup);
                    candidate.PropertyChanged += (s, e) =>
                    {
                        if (e?.PropertyName == nameof(RuleImportCandidate.IsSelected))
                        {
                            NotifyImportSelectionChanged();
                        }
                    };
                    ImportCandidates.Add(candidate);
                }

                NotifyImportSelectionChanged();
                IsImportModalOpen = true;
            }
        }
        catch (Exception ex)
        {
            SetTransientFeedback($"Import error: {ex.Message}");
        }
    }

    public void NotifyImportSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedImportCount));
        OnPropertyChanged(nameof(TotalImportCount));
        OnPropertyChanged(nameof(ImportSummaryText));
        OnPropertyChanged(nameof(ImportButtonText));
    }

    public void ConfirmImport()
    {
        var selected = ImportCandidates.Where(c => c.IsSelected).Select(c => c.Rule).ToList();
        if (selected.Count == 0)
        {
            IsImportModalOpen = false;
            return;
        }

        int addedCount = 0;
        foreach (var rule in selected)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || _rules.Any(r => r.Id == rule.Id))
            {
                rule.Id = Guid.NewGuid().ToString("N");
            }

            AttachRuleListener(rule);
            _rules.Add(rule);
            _configService.Config.Rules.Add(rule);
            addedCount++;
        }

        _configService.Save();
        NotifyRulesCountChanged();
        IsImportModalOpen = false;
        ImportCandidates.Clear();
        SetTransientFeedback($"✓ Successfully imported {addedCount} rule{(addedCount == 1 ? "" : "s")}.");
    }

    public void CancelImport()
    {
        IsImportModalOpen = false;
        ImportCandidates.Clear();
    }

    public void SelectAllImport(bool select)
    {
        foreach (var c in ImportCandidates)
        {
            c.IsSelected = select;
        }
        NotifyImportSelectionChanged();
    }
}

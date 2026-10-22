using System.Collections.ObjectModel;
using System.Windows.Input;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;

namespace GotifyWin.ViewModels;

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

    public int RecommendationsCount => _recommendations.Count;
    public string RecommendationsSummaryText => $"{RecommendationsCount} suggestion{(RecommendationsCount == 1 ? "" : "s")} ready";

    public int ActiveRulesCount => _rules.Count(r => r.Enabled);
    public int PausedRulesCount => _rules.Count(r => !r.Enabled);
    public int TotalRulesCount => _rules.Count;
    public bool HasActiveRules => _rules.Count > 0;
    public string RulesSummaryText => _rules.Count == 0 ? "0 rules" : PausedRulesCount == 0 ? $"{ActiveRulesCount} active" : $"{ActiveRulesCount} active • {PausedRulesCount} paused";

    public ICommand AddRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand ToggleRuleCommand { get; }
    public ICommand ToggleAdvancedCommand { get; }
    public ICommand ToggleRecommendationsCommand { get; }
    public ICommand ToggleConfiguredRulesCommand { get; }
    public ICommand ReanalyzeCommand { get; }

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
        DeleteRuleCommand = new RelayCommand<NotificationRule>(DeleteRule);
        ToggleRuleCommand = new RelayCommand<NotificationRule>(ToggleRule);
        ToggleAdvancedCommand = new RelayCommand(() => IsAdvancedExpanded = !IsAdvancedExpanded);
        ToggleRecommendationsCommand = new RelayCommand(() => IsRecommendationsExpanded = !IsRecommendationsExpanded);
        ToggleConfiguredRulesCommand = new RelayCommand(() => IsConfiguredRulesExpanded = !IsConfiguredRulesExpanded);
        ReanalyzeCommand = new RelayCommand(async () => await ReanalyzeHistoryAsync());

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

            // If local feed has fewer than 5 messages (e.g. cleared by user), try to fetch from server if available
            if (messages.Count() < 5 && _historyFetcher != null)
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

            // If still fewer than 5 messages, RETAIN current recommendations so they are not wiped out
            if (messages.Count() < 5)
            {
                ReanalyzeButtonText = "✓ Up to date";
                await Task.Delay(1400);
                return;
            }

            var recs = _patternAnalyzer.Analyze(messages, _configService.Config, threshold: 5);
            if (recs.Count > 0 || messages.Count() >= 5)
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
    }

    private void DeleteRule(NotificationRule? rule)
    {
        if (rule == null) return;
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
}

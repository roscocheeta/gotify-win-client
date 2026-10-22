using System.Collections.ObjectModel;
using System.Windows.Input;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;

namespace GotifyWin.ViewModels;

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

    private readonly ObservableCollection<NotificationRule> _rules = new();
    private readonly ObservableCollection<RecommendationItemViewModel> _recommendations = new();

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
    public int ActiveRulesCount => _rules.Count;
    public bool HasActiveRules => _rules.Count > 0;

    public ICommand AddRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand ToggleRuleCommand { get; }
    public ICommand ToggleAdvancedCommand { get; }
    public ICommand ReanalyzeCommand { get; }

    public RulesViewModel(
        IConfigService configService,
        INotificationPatternAnalyzer? patternAnalyzer = null,
        Func<IEnumerable<GotifyMessage>>? messageSource = null)
    {
        _configService = configService;
        _patternAnalyzer = patternAnalyzer;
        _messageSource = messageSource;

        AddRuleCommand = new RelayCommand(AddRule);
        DeleteRuleCommand = new RelayCommand<NotificationRule>(DeleteRule);
        ToggleRuleCommand = new RelayCommand<NotificationRule>(ToggleRule);
        ToggleAdvancedCommand = new RelayCommand(() => IsAdvancedExpanded = !IsAdvancedExpanded);
        ReanalyzeCommand = new RelayCommand(ReanalyzeHistory);

        LoadRules();
    }

    public void PrepopulateFromMessage(GotifyMessage message)
    {
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
        OnPropertyChanged(nameof(HasRecommendations));
    }

    public void ReanalyzeHistory()
    {
        if (_patternAnalyzer == null || _messageSource == null) return;
        var messages = _messageSource();
        var recs = _patternAnalyzer.Analyze(messages, _configService.Config, threshold: 5);
        SetRecommendations(recs);
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
            TargetField = recommendation.SuggestedField,
            Operator = recommendation.SuggestedOperator,
            Pattern = recommendation.SuggestedPattern,
            Action = action,
            CaseSensitive = false
        };

        _rules.Add(rule);
        _configService.Config.Rules.Add(rule);
        _configService.Save();
        OnPropertyChanged(nameof(ActiveRulesCount));
        OnPropertyChanged(nameof(HasActiveRules));

        // Remove from recommendations
        var match = _recommendations.FirstOrDefault(r => r.Id == recommendation.Id);
        if (match != null)
        {
            _recommendations.Remove(match);
            OnPropertyChanged(nameof(HasRecommendations));
        }
    }

    public void DismissRecommendation(RuleRecommendation recommendation)
    {
        var match = _recommendations.FirstOrDefault(r => r.Id == recommendation.Id);
        if (match != null)
        {
            _recommendations.Remove(match);
            OnPropertyChanged(nameof(HasRecommendations));
        }
    }

    private void LoadRules()
    {
        _rules.Clear();
        foreach (var rule in _configService.Config.Rules)
        {
            _rules.Add(rule);
        }
        OnPropertyChanged(nameof(ActiveRulesCount));
        OnPropertyChanged(nameof(HasActiveRules));
    }

    private void AddRule()
    {
        if (string.IsNullOrWhiteSpace(NewRulePattern))
            return;

        var name = string.IsNullOrWhiteSpace(NewRuleName)
            ? $"{NewRuleField} {NewRuleOperator} '{NewRulePattern}'"
            : NewRuleName.Trim();

        var rule = new NotificationRule
        {
            Name = name,
            Enabled = true,
            TargetField = NewRuleField,
            Operator = NewRuleOperator,
            Pattern = NewRulePattern.Trim(),
            Action = NewRuleAction,
            CaseSensitive = NewRuleCaseSensitive
        };

        _rules.Add(rule);
        _configService.Config.Rules.Add(rule);
        _configService.Save();
        OnPropertyChanged(nameof(ActiveRulesCount));
        OnPropertyChanged(nameof(HasActiveRules));

        // Reset inputs
        NewRuleName = string.Empty;
        NewRulePattern = string.Empty;
    }

    private void DeleteRule(NotificationRule? rule)
    {
        if (rule == null) return;
        _rules.Remove(rule);
        _configService.Config.Rules.Remove(rule);
        _configService.Save();
        OnPropertyChanged(nameof(ActiveRulesCount));
        OnPropertyChanged(nameof(HasActiveRules));
    }

    private void ToggleRule(NotificationRule? rule)
    {
        if (rule == null) return;
        rule.Enabled = !rule.Enabled;
        _configService.Save();
        OnPropertyChanged(nameof(Rules));
    }
}

using System.Collections.ObjectModel;
using System.Windows.Input;
using GotifyWin.Models.Config;
using GotifyWin.Services;

namespace GotifyWin.ViewModels;

public class RulesViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly ObservableCollection<NotificationRule> _rules = new();

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

    private RuleAction _newRuleAction = RuleAction.Drop;
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

    public ICommand AddRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand ToggleRuleCommand { get; }

    public RulesViewModel(IConfigService configService)
    {
        _configService = configService;

        AddRuleCommand = new RelayCommand(AddRule);
        DeleteRuleCommand = new RelayCommand<NotificationRule>(DeleteRule);
        ToggleRuleCommand = new RelayCommand<NotificationRule>(ToggleRule);

        LoadRules();
    }

    private void LoadRules()
    {
        _rules.Clear();
        foreach (var rule in _configService.Config.Rules)
        {
            _rules.Add(rule);
        }
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
    }

    private void ToggleRule(NotificationRule? rule)
    {
        if (rule == null) return;
        rule.Enabled = !rule.Enabled;
        _configService.Save();
        OnPropertyChanged(nameof(Rules));
    }
}

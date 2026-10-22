using System.ComponentModel;

namespace GotifyWin.Models.Config;

public class NotificationRule : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Rule";

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled != value)
            {
                _enabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
            }
        }
    }

    public string StatusLabel => Enabled ? "Active" : "Paused";

    public RuleField TargetField { get; set; } = RuleField.Title;
    public RuleOperator Operator { get; set; } = RuleOperator.Contains;
    public string Pattern { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; } = false;
    public RuleAction Action { get; set; } = RuleAction.Drop;
}

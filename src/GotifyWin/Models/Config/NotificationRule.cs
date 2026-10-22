namespace GotifyWin.Models.Config;

public class NotificationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Rule";
    public bool Enabled { get; set; } = true;
    public RuleField TargetField { get; set; } = RuleField.Title;
    public RuleOperator Operator { get; set; } = RuleOperator.Contains;
    public string Pattern { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; } = false;
    public RuleAction Action { get; set; } = RuleAction.Drop;
}

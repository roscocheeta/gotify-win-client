using GotifyWin.Models.Config;

namespace GotifyWin.Models;

public class RuleRecommendation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SuggestedPattern { get; set; } = string.Empty;
    public RuleField SuggestedField { get; set; } = RuleField.Title;
    public RuleOperator SuggestedOperator { get; set; } = RuleOperator.Contains;
    public RuleAction SuggestedAction { get; set; } = RuleAction.SilentLog;
    public int MatchCount { get; set; }
    public List<GotifyMessage> SampleMessages { get; set; } = new();
}

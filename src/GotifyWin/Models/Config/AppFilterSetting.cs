namespace GotifyWin.Models.Config;

public class AppFilterSetting
{
    public long AppId { get; set; }
    public string AppName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int MinPriority { get; set; } = 0;
    public string? CustomSoundPath { get; set; }
    public bool OverrideGlobalDnd { get; set; } = false;
}

namespace GotifyWin.Models.Config;

public class GotifyClientConfig
{
    public string ServerUrl { get; set; } = string.Empty;
    public string ClientToken { get; set; } = string.Empty;

    public bool StartWithWindows { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public int MaxHistoryCount { get; set; } = 200;

    public int GlobalMinPriority { get; set; } = 0;

    public PresentationSettings Presentation { get; set; } = new();
    public DndSettings Dnd { get; set; } = new();

    public Dictionary<long, AppFilterSetting> AppFilters { get; set; } = new();
    public List<NotificationRule> Rules { get; set; } = new();
}

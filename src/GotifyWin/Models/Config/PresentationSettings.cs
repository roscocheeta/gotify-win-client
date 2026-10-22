namespace GotifyWin.Models.Config;

public class PresentationSettings
{
    public NotificationMode Mode { get; set; } = NotificationMode.CustomHud;

    // Custom HUD Settings
    public HudPosition Position { get; set; } = HudPosition.BottomRight;
    public double DisplayDurationSeconds { get; set; } = 5.0;
    public bool ShowCountdownBar { get; set; } = true;
    public bool PauseOnHover { get; set; } = true;
    public double Width { get; set; } = 360.0;
    public int MaxBodyLines { get; set; } = 4;

    // Audio Settings
    public bool PlaySound { get; set; } = true;
    public string? CustomSoundFilePath { get; set; } = null;
    public bool MuteSoundsDuringQuietHours { get; set; } = true;

    // Debounce / Anti-Flap
    public bool EnableDebounce { get; set; } = true;
    public int DebounceWindowSeconds { get; set; } = 10;
}

namespace GotifyWin.Models.Config;

public class DndSettings
{
    public bool IsManualDndActive { get; set; } = false;
    public DateTime? ManualDndUntilUtc { get; set; } = null;

    // Emergency Bypass: Messages with Priority >= this threshold will break through DND
    public int EmergencyBypassPriority { get; set; } = 8;
    public bool EnableEmergencyBypass { get; set; } = true;

    // Scheduled Quiet Hours
    public bool QuietHoursEnabled { get; set; } = false;
    public TimeSpan QuietHoursStart { get; set; } = new TimeSpan(22, 0, 0); // 10:00 PM
    public TimeSpan QuietHoursEnd { get; set; } = new TimeSpan(7, 0, 0);   // 7:00 AM

    // Win32 Fullscreen & Gaming Suppression
    public bool SuppressOnFullscreenApps { get; set; } = true;

    // Game Session Retention (Alt-Tab Persistence)
    // Retains DND suppression when alt-tabbing away from a running game as long as the game process is running
    public bool RetainGameDndOnAltTab { get; set; } = true;
    public int GameAltTabRetentionSeconds { get; set; } = 300; // 5 minutes retention window
}

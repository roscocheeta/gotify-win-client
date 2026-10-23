namespace GotifyWin.Models.Config;

public enum NotificationMode
{
    CustomHud = 0,
    NativeToast = 1,
    Both = 2
}

public enum HudPosition
{
    BottomRight = 0,
    TopRight = 1,
    TopCenter = 2,
    BottomCenter = 3,
    TopLeft = 4,
    BottomLeft = 5
}

public enum RuleField
{
    Title = 0,
    Message = 1,
    AppName = 2
}

public enum RuleOperator
{
    Contains = 0,
    DoesNotContain = 1,
    StartsWith = 2,
    EndsWith = 3,
    ExactMatch = 4,
    RegexMatch = 5
}

public enum RuleAction
{
    Drop = 0,               // Discard message completely, do not show notification or log
    SilentLog = 1,          // Store message in history without showing notification popup or playing sound
    AlwaysNotify = 2,       // Show notification popup and play sound even if DND or app threshold would otherwise mute it
    UrgentNotification = 2, // Alias for AlwaysNotify
    UrgentAlert = 2         // Alias for backward compatibility with existing saved config files
}

public enum FilterDecision
{
    Notify = 0,     // Show notification popup & play sound
    Alert = 0,      // Alias for backward compatibility
    SilentLog = 1,  // Save in message history, but no popup/sound
    Drop = 2        // Discard message completely
}

public enum ConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3
}

public enum AppThemeMode
{
    System = 0,
    Dark = 1,
    Light = 2
}

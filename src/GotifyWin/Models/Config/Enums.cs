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
    Drop = 0,       // Discard completely, do not show or log
    SilentLog = 1,  // Store in history feed without showing popup or playing sound
    UrgentAlert = 2 // Show popup and play sound even if DND or app threshold would otherwise block it
}

public enum FilterDecision
{
    Alert = 0,      // Show notification popup & play sound
    SilentLog = 1,  // Save in history, but no popup/sound
    Drop = 2        // Drop completely
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

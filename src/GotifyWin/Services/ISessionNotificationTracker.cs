using GotifyWin.Models;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public interface ISessionNotificationTracker
{
    bool IsLocked { get; }
    int MissedCount { get; }
    void OnSessionLocked();
    GotifyMessage? OnSessionUnlocked(PresentationSettings settings);
    void RecordNotification(GotifyMessage message, int minPriority = 1);
    void Reset();
}

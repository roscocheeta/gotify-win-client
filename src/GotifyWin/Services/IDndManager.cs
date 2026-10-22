using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public interface IDndManager
{
    bool IsDndActive(DndSettings settings);
    bool IsQuietHours(DndSettings settings, DateTime? currentTime = null);
    bool IsFullscreenAppRunning();
    void SetManualDnd(DndSettings settings, TimeSpan? duration);
    void DisableManualDnd(DndSettings settings);
    string GetStatusDescription(DndSettings settings);
}

using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public enum FullscreenState
{
    None,
    Game,          // Direct3D / DirectX Game
    Presentation,  // Windows presentation / projector
    Busy           // Fullscreen media / busy
}

public interface IDndManager
{
    bool IsDndActive(DndSettings settings);
    bool IsQuietHours(DndSettings settings, DateTime? currentTime = null);
    bool IsFullscreenAppRunning();
    bool IsFullscreenAppRunning(DndSettings settings);
    FullscreenState GetFullscreenState();
    FullscreenState GetFullscreenState(DndSettings settings);
    void SetManualDnd(DndSettings settings, TimeSpan? duration);
    void DisableManualDnd(DndSettings settings);
    string GetStatusDescription(DndSettings settings);
}

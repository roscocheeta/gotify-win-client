using GotifyWin.Models;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public interface ISoundService
{
    // Legacy signatures
    void PlayNotification(string? customSoundPath = null, bool isMuted = false);
    void PlayAlert(string? customSoundPath = null, bool isMuted = false);

    // Modern priority-aware signature
    void PlayNotification(GotifyMessage message, AudioProfileConfig audioConfig, AppFilterSetting? appFilter = null, bool isMuted = false);

    // Interactive preview methods
    void PlayPreview(SoundPreset preset, SoundTier tier, double volume);
    void PlayCustomFilePreview(string filePath, double volume);

    // Audio validation helper
    bool ValidateSoundFile(string filePath, out string? errorMessage);
}

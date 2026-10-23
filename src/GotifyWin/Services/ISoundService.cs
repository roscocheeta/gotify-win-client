namespace GotifyWin.Services;

public interface ISoundService
{
    void PlayNotification(string? customSoundPath = null, bool isMuted = false);
    void PlayAlert(string? customSoundPath = null, bool isMuted = false);
}

namespace GotifyWin.Services;

public interface ISoundService
{
    void PlayAlert(string? customSoundPath = null, bool isMuted = false);
}

using System.IO;
using System.Media;

namespace GotifyWin.Services;

public class SoundService : ISoundService
{
    public void PlayAlert(string? customSoundPath = null, bool isMuted = false)
    {
        if (isMuted) return;

        try
        {
            if (!string.IsNullOrWhiteSpace(customSoundPath) && File.Exists(customSoundPath))
            {
                using var player = new SoundPlayer(customSoundPath);
                player.Play();
                return;
            }

            SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SoundService] Failed to play audio alert: {ex.Message}");
        }
    }
}

using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public class AudioRateLimiter
{
    private readonly object _lock = new();
    private DateTime _lastPlayUtc = DateTime.MinValue;
    private SoundTier _lastTier = SoundTier.Low;

    public bool ShouldPlay(SoundTier tier, int windowMs = 150)
    {
        if (windowMs <= 0) return true;

        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var elapsedMs = (now - _lastPlayUtc).TotalMilliseconds;

            if (elapsedMs >= windowMs)
            {
                _lastPlayUtc = now;
                _lastTier = tier;
                return true;
            }

            // If incoming sound has strictly higher urgency/tier than the one currently cooling down, allow it
            if (tier > _lastTier)
            {
                _lastPlayUtc = now;
                _lastTier = tier;
                return true;
            }

            return false;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _lastPlayUtc = DateTime.MinValue;
            _lastTier = SoundTier.Low;
        }
    }
}

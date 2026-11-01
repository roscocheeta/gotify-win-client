using GotifyWin.Models;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public class SessionNotificationTracker : ISessionNotificationTracker
{
    private readonly object _lock = new();
    private readonly List<GotifyMessage> _missedMessages = new();
    private DateTime? _lockedAtUtc;
    private readonly TimeSpan _minLockDuration;

    public bool IsLocked { get; private set; }

    public int MissedCount
    {
        get
        {
            lock (_lock)
            {
                return _missedMessages.Count;
            }
        }
    }

    public SessionNotificationTracker(TimeSpan? minLockDuration = null)
    {
        _minLockDuration = minLockDuration ?? TimeSpan.FromSeconds(10);
    }

    public void OnSessionLocked()
    {
        lock (_lock)
        {
            IsLocked = true;
            _lockedAtUtc = DateTime.UtcNow;
            _missedMessages.Clear();
        }
    }

    public void RecordNotification(GotifyMessage message, int minPriority = 1)
    {
        lock (_lock)
        {
            if (!IsLocked) return;
            if (message.Priority < minPriority) return;

            _missedMessages.Add(message);
        }
    }

    public GotifyMessage? OnSessionUnlocked(PresentationSettings settings)
    {
        lock (_lock)
        {
            if (!IsLocked)
            {
                return null;
            }

            IsLocked = false;
            var lockedDuration = _lockedAtUtc.HasValue ? DateTime.UtcNow - _lockedAtUtc.Value : TimeSpan.Zero;
            _lockedAtUtc = null;

            if (!settings.EnableMissedSummaryOnUnlock)
            {
                _missedMessages.Clear();
                return null;
            }

            if (lockedDuration < _minLockDuration || _missedMessages.Count == 0)
            {
                _missedMessages.Clear();
                return null;
            }

            var count = _missedMessages.Count;
            var appNames = _missedMessages
                .Select(m => m.DisplayAppName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string appsSummary;
            if (appNames.Count == 0)
            {
                appsSummary = "Gotify";
            }
            else if (appNames.Count <= 3)
            {
                appsSummary = string.Join(", ", appNames);
            }
            else
            {
                appsSummary = $"{string.Join(", ", appNames.Take(3))} +{appNames.Count - 3} more";
            }

            var maxPriority = _missedMessages.Max(m => m.Priority);
            _missedMessages.Clear();

            return new GotifyMessage
            {
                Id = DateTime.UtcNow.Ticks,
                AppId = 0,
                Title = $"Missed Notifications ({count})",
                Message = count == 1
                    ? $"You missed 1 notification from {appsSummary} while away."
                    : $"You missed {count} notifications from {appsSummary} while away.",
                Priority = Math.Max(6, maxPriority),
                Date = DateTime.UtcNow,
                ApplicationName = "GotifyWin"
            };
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            IsLocked = false;
            _lockedAtUtc = null;
            _missedMessages.Clear();
        }
    }
}

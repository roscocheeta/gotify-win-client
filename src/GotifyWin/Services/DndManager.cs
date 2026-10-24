using System.Runtime.InteropServices;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public class DndManager : IDndManager
{
    private enum USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SHQueryUserNotificationState(out USER_NOTIFICATION_STATE pquns);

    public bool IsDndActive(DndSettings settings)
    {
        // 1. Check manual DND
        if (settings.IsManualDndActive)
        {
            if (settings.ManualDndUntilUtc.HasValue)
            {
                if (DateTime.UtcNow < settings.ManualDndUntilUtc.Value)
                {
                    return true;
                }
                else
                {
                    // Expired
                    settings.IsManualDndActive = false;
                    settings.ManualDndUntilUtc = null;
                }
            }
            else
            {
                // Indefinite manual DND
                return true;
            }
        }

        // 2. Check scheduled quiet hours
        if (settings.QuietHoursEnabled && IsQuietHours(settings))
        {
            return true;
        }

        // 3. Check fullscreen gaming / presentation mode
        if (settings.SuppressOnFullscreenApps && IsFullscreenAppRunning())
        {
            return true;
        }

        return false;
    }

    public bool IsQuietHours(DndSettings settings, DateTime? currentTime = null)
    {
        if (!settings.QuietHoursEnabled)
            return false;

        var now = currentTime ?? DateTime.Now;
        var timeOfDay = now.TimeOfDay;

        var start = settings.QuietHoursStart;
        var end = settings.QuietHoursEnd;

        if (start > end)
        {
            // Crosses midnight, e.g. 22:00 to 07:00
            return timeOfDay >= start || timeOfDay < end;
        }
        else
        {
            // Same day, e.g. 09:00 to 17:00
            return timeOfDay >= start && timeOfDay < end;
        }
    }

    public FullscreenState GetFullscreenState()
    {
        try
        {
            var hr = SHQueryUserNotificationState(out var state);
            if (hr == 0) // S_OK
            {
                return state switch
                {
                    USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN => FullscreenState.Game,
                    USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE => FullscreenState.Presentation,
                    USER_NOTIFICATION_STATE.QUNS_BUSY => FullscreenState.Busy,
                    _ => FullscreenState.None
                };
            }
        }
        catch
        {
            // Ignore on platforms where shell32 is unavailable
        }

        return FullscreenState.None;
    }

    public bool IsFullscreenAppRunning()
    {
        return GetFullscreenState() != FullscreenState.None;
    }

    public void SetManualDnd(DndSettings settings, TimeSpan? duration)
    {
        settings.IsManualDndActive = true;
        settings.ManualDndUntilUtc = duration.HasValue ? DateTime.UtcNow.Add(duration.Value) : null;
    }

    public void DisableManualDnd(DndSettings settings)
    {
        settings.IsManualDndActive = false;
        settings.ManualDndUntilUtc = null;
    }

    public string GetStatusDescription(DndSettings settings)
    {
        if (settings.IsManualDndActive)
        {
            if (settings.ManualDndUntilUtc.HasValue)
            {
                var remaining = settings.ManualDndUntilUtc.Value - DateTime.UtcNow;
                if (remaining.TotalMinutes > 0)
                {
                    return $"DND on (until {settings.ManualDndUntilUtc.Value.ToLocalTime():HH:mm})";
                }
            }
            return "DND on (Indefinite)";
        }

        if (settings.QuietHoursEnabled && IsQuietHours(settings))
        {
            return $"Quiet Hours active ({settings.QuietHoursStart:hh\\:mm} - {settings.QuietHoursEnd:hh\\:mm})";
        }

        if (settings.SuppressOnFullscreenApps)
        {
            var fs = GetFullscreenState();
            if (fs == FullscreenState.Game)
                return "DND on (DirectX Game detected)";
            if (fs == FullscreenState.Presentation)
                return "DND on (Presentation mode detected)";
            if (fs == FullscreenState.Busy)
                return "DND on (Media / Busy detected)";
        }

        return "DND: Off";
    }
}

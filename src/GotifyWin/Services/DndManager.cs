using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const int GWL_STYLE = -16;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private readonly object _lock = new();
    private int _lastActiveGameProcessId;
    private string? _lastActiveGameProcessName;
    private DateTime _lastGameActiveUtc = DateTime.MinValue;

    // Testing / Mock hooks for automated tests
    internal Func<FullscreenState>? StateProbe { get; set; }
    internal Func<int>? WinNotificationStateProbe { get; set; }
    internal Func<IntPtr>? ForegroundWindowProbe { get; set; }
    internal Func<int, bool>? ProcessRunningProbe { get; set; }

    public int TrackedGameProcessId
    {
        get { lock (_lock) { return _lastActiveGameProcessId; } }
    }

    public string? TrackedGameProcessName
    {
        get { lock (_lock) { return _lastActiveGameProcessName; } }
    }

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

        // 3. Check fullscreen gaming / presentation mode / Alt-Tabbed game
        if (settings.SuppressOnFullscreenApps && IsFullscreenAppRunning(settings))
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

    public bool IsFullscreenAppRunning()
    {
        return IsFullscreenAppRunning(new DndSettings());
    }

    public bool IsFullscreenAppRunning(DndSettings settings)
    {
        return GetFullscreenState(settings) != FullscreenState.None;
    }

    public FullscreenState GetFullscreenState()
    {
        return GetFullscreenState(new DndSettings());
    }

    public FullscreenState GetFullscreenState(DndSettings settings)
    {
        if (StateProbe != null)
        {
            return StateProbe.Invoke();
        }

        // 1. Query Win32 Notification State (catches exclusive DirectX D3D fullscreen & presentation mode)
        try
        {
            int hr = 0;
            USER_NOTIFICATION_STATE state;

            if (WinNotificationStateProbe != null)
            {
                state = (USER_NOTIFICATION_STATE)WinNotificationStateProbe.Invoke();
            }
            else
            {
                hr = SHQueryUserNotificationState(out state);
            }

            if (hr == 0) // S_OK
            {
                if (state == USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN)
                {
                    RecordActiveGameForeground();
                    return FullscreenState.Game;
                }
                if (state == USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
                {
                    return FullscreenState.Presentation;
                }
                if (state == USER_NOTIFICATION_STATE.QUNS_BUSY)
                {
                    return FullscreenState.Busy;
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine($"[DndManager] SHQueryUserNotificationState unavailable: {ex.Message}");
        }

        // 2. Check for Borderless Fullscreen Windows (Window fills entire monitor and lacks WS_CAPTION)
        if (IsForegroundWindowBorderlessFullscreen(out var isGameApp, out var processId, out var processName))
        {
            if (isGameApp || processId > 0)
            {
                RecordActiveGameForeground(processId, processName);
                return FullscreenState.Game;
            }
            return FullscreenState.Busy;
        }

        // 3. Alt-Tab Retention (Issue #24): Check if a game was recently active and its process is still running
        if (settings.RetainGameDndOnAltTab)
        {
            lock (_lock)
            {
                if (_lastActiveGameProcessId > 0)
                {
                    bool isRunning = false;
                    if (ProcessRunningProbe != null)
                    {
                        isRunning = ProcessRunningProbe.Invoke(_lastActiveGameProcessId);
                    }
                    else
                    {
                        try
                        {
                            using var proc = Process.GetProcessById(_lastActiveGameProcessId);
                            isRunning = !proc.HasExited;
                        }
                        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                        {
                            isRunning = false;
                        }
                    }

                    if (isRunning)
                    {
                        var elapsedSeconds = (DateTime.UtcNow - _lastGameActiveUtc).TotalSeconds;
                        if (elapsedSeconds <= settings.GameAltTabRetentionSeconds)
                        {
                            return FullscreenState.Game;
                        }
                    }

                    // Process has exited or grace period expired
                    _lastActiveGameProcessId = 0;
                    _lastActiveGameProcessName = null;
                }
            }
        }

        return FullscreenState.None;
    }

    private void RecordActiveGameForeground(int processId = 0, string? processName = null)
    {
        lock (_lock)
        {
            if (processId <= 0)
            {
                var fgHwnd = ForegroundWindowProbe != null ? ForegroundWindowProbe() : GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fgHwnd, out var pid);
                    processId = (int)pid;
                    if (processId > 0)
                    {
                        try
                        {
                            using var proc = Process.GetProcessById(processId);
                            processName = proc.ProcessName;
                        }
                        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                        {
                            processName = null;
                        }
                    }
                }
            }

            if (processId > 0)
            {
                _lastActiveGameProcessId = processId;
                _lastActiveGameProcessName = processName ?? $"Process #{processId}";
                _lastGameActiveUtc = DateTime.UtcNow;
            }
        }
    }

    private bool IsForegroundWindowBorderlessFullscreen(out bool isGameApp, out int processId, out string? processName)
    {
        isGameApp = false;
        processId = 0;
        processName = null;

        var fgHwnd = ForegroundWindowProbe != null ? ForegroundWindowProbe() : GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero) return false;

        // Ignore standard shell/desktop windows
        var sb = new StringBuilder(256);
        if (GetClassName(fgHwnd, sb, 256) > 0)
        {
            var className = sb.ToString();
            if (className is "Shell_TrayWnd" or "WorkerW" or "Progman" or "MultitaskingViewFrame" or "Windows.UI.Core.CoreWindow")
            {
                return false;
            }
        }

        if (GetWindowRect(fgHwnd, out var windowRect) == 0) return false;

        var hMonitor = MonitorFromWindow(fgHwnd, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return false;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMonitor, ref mi)) return false;

        // Check if window bounds cover or exceed the monitor rectangle
        bool coversMonitor = windowRect.Left <= mi.rcMonitor.Left &&
                             windowRect.Top <= mi.rcMonitor.Top &&
                             windowRect.Right >= mi.rcMonitor.Right &&
                             windowRect.Bottom >= mi.rcMonitor.Bottom;

        if (!coversMonitor) return false;

        // Inspect window style: borderless windows lack WS_CAPTION
        long style = GetWindowLongPtr(fgHwnd, GWL_STYLE).ToInt64();
        bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;

        if (!hasCaption)
        {
            GetWindowThreadProcessId(fgHwnd, out var pid);
            processId = (int)pid;
            if (processId > 0)
            {
                try
                {
                    using var proc = Process.GetProcessById(processId);
                    processName = proc.ProcessName;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    processName = null;
                }
            }
            isGameApp = true;
            return true;
        }

        return false;
    }

    internal void SetActiveGameSessionForTest(int processId, string processName, DateTime lastActiveUtc)
    {
        lock (_lock)
        {
            _lastActiveGameProcessId = processId;
            _lastActiveGameProcessName = processName;
            _lastGameActiveUtc = lastActiveUtc;
        }
    }

    internal void ClearActiveGameSession()
    {
        lock (_lock)
        {
            _lastActiveGameProcessId = 0;
            _lastActiveGameProcessName = null;
            _lastGameActiveUtc = DateTime.MinValue;
        }
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
            var fs = GetFullscreenState(settings);
            if (fs == FullscreenState.Game)
            {
                lock (_lock)
                {
                    if (!string.IsNullOrWhiteSpace(_lastActiveGameProcessName))
                    {
                        return $"DND on (Game active: {_lastActiveGameProcessName})";
                    }
                }
                return "DND on (DirectX Game detected)";
            }
            if (fs == FullscreenState.Presentation)
                return "DND on (Presentation mode detected)";
            if (fs == FullscreenState.Busy)
                return "DND on (Media / Fullscreen detected)";
        }

        return "DND: Off";
    }
}

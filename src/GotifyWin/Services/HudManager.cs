using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Animation;
using Application = System.Windows.Application;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Views;

namespace GotifyWin.Services;

public interface IHudManager : IDisposable
{
    void ShowNotification(GotifyMessage message, PresentationSettings settings, Action<string>? onUrlOpen);
    void DismissAll();
}

public class HudManager : IHudManager
{
    public const int MaxConcurrentVisible = 3;
    private const double HudSpacing = 6.0;
    private const double Margin = 20.0;
    private const double DefaultSlotHeight = 160.0;

    public record PendingHudItem(
        GotifyMessage Message,
        PresentationSettings Settings,
        Action<string>? OnUrlOpen,
        DateTime EnqueuedAtUtc,
        double DurationMs);

    private readonly List<NotificationHudWindow> _activeWindows = new();
    private readonly Queue<PendingHudItem> _pendingQueue = new();
    private readonly object _lock = new();
    private bool _isSessionLocked;
    private bool _disposed;

    public int ActiveCount
    {
        get { lock (_lock) { return _activeWindows.Count; } }
    }

    public int QueuedCount
    {
        get { lock (_lock) { return _pendingQueue.Count; } }
    }

    public bool IsSessionLocked
    {
        get { lock (_lock) { return _isSessionLocked; } }
        set { lock (_lock) { _isSessionLocked = value; } }
    }

    public HudManager()
    {
        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch { }
    }

    private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
        {
            lock (_lock)
            {
                _isSessionLocked = true;
            }
        }
        else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
        {
            UiDispatcher.RunOnUI(() =>
            {
                lock (_lock)
                {
                    _isSessionLocked = false;
                    HandleSessionUnlock();
                }
            });
        }
    }

    public void HandleSessionUnlock(HudPosition? position = null)
    {
        // 1. Check all active windows for expiration or resynchronization
        var windows = _activeWindows.ToList();
        foreach (var w in windows)
        {
            try
            {
                w.CheckExpiration();
            }
            catch { }
        }

        // 2. Discard stale/expired items from queue
        PurgeExpiredQueue();

        // 3. Dequeue fresh items up to MaxConcurrentVisible
        while (_pendingQueue.Count > 0 && _activeWindows.Count < MaxConcurrentVisible)
        {
            var next = _pendingQueue.Dequeue();
            SpawnHudWindow(next.Message, next.Settings, next.OnUrlOpen);
        }

        if (position.HasValue)
        {
            RestackWindows(position.Value);
        }
    }

    public int PurgeExpiredQueue()
    {
        var validItems = new List<PendingHudItem>();
        var now = DateTime.UtcNow;
        int purged = 0;

        while (_pendingQueue.Count > 0)
        {
            var item = _pendingQueue.Dequeue();
            var maxAgeMs = item.DurationMs + 3000.0;
            if ((now - item.EnqueuedAtUtc).TotalMilliseconds < maxAgeMs)
            {
                validItems.Add(item);
            }
            else
            {
                purged++;
            }
        }

        foreach (var item in validItems)
        {
            _pendingQueue.Enqueue(item);
        }

        return purged;
    }

    public void DismissAll()
    {
        UiDispatcher.RunOnUI(() =>
        {
            lock (_lock)
            {
                _pendingQueue.Clear();
                var windows = _activeWindows.ToList();
                _activeWindows.Clear();
                foreach (var w in windows)
                {
                    try
                    {
                        w.Close();
                    }
                    catch { }
                }
            }
        });
    }

    public void ShowNotification(GotifyMessage message, PresentationSettings settings, Action<string>? onUrlOpen)
    {
        UiDispatcher.RunOnUI(() =>
        {
            lock (_lock)
            {
                var durationMs = Math.Max(2.0, settings.DisplayDurationSeconds) * 1000.0;

                // When workstation is locked, queue instead of creating un-viewable HWNDs on background desktop
                if (_isSessionLocked)
                {
                    if (_pendingQueue.Count < 30)
                    {
                        _pendingQueue.Enqueue(new PendingHudItem(message, settings, onUrlOpen, DateTime.UtcNow, durationMs));
                    }
                    return;
                }

                if (_activeWindows.Count < MaxConcurrentVisible)
                {
                    SpawnHudWindow(message, settings, onUrlOpen);
                }
                else
                {
                    // Queue excess notifications so active banners are not instantly destroyed
                    if (_pendingQueue.Count < 30)
                    {
                        _pendingQueue.Enqueue(new PendingHudItem(message, settings, onUrlOpen, DateTime.UtcNow, durationMs));
                    }
                }
            }
        });
    }

    private void SpawnHudWindow(GotifyMessage message, PresentationSettings settings, Action<string>? onUrlOpen)
    {
        var hud = new NotificationHudWindow();
        hud.Setup(message, settings, onUrlOpen);

        int slotIndex = _activeWindows.Count;
        _activeWindows.Add(hud);

        // Position window before showing
        PositionHudWindow(hud, slotIndex, settings.Position);

        hud.Closed += (s, e) =>
        {
            UiDispatcher.RunOnUI(() =>
            {
                lock (_lock)
                {
                    _activeWindows.Remove(hud);

                    // Dequeue next pending notification if available and not expired
                    while (_pendingQueue.Count > 0 && _activeWindows.Count < MaxConcurrentVisible)
                    {
                        var candidate = _pendingQueue.Dequeue();
                        var maxAgeMs = candidate.DurationMs + 3000.0;
                        if ((DateTime.UtcNow - candidate.EnqueuedAtUtc).TotalMilliseconds < maxAgeMs)
                        {
                            SpawnHudWindow(candidate.Message, candidate.Settings, candidate.OnUrlOpen);
                            break;
                        }
                    }

                    // Restack remaining active windows
                    RestackWindows(settings.Position);
                }
            });
        };

        hud.Show();
    }

    private double GetWindowHeight(NotificationHudWindow window)
    {
        return window.ActualHeight > 50.0 ? window.ActualHeight : DefaultSlotHeight;
    }

    private double CalculateSlotTop(int slotIndex, HudPosition position)
    {
        var workArea = SystemParameters.WorkArea;

        if (position == HudPosition.TopRight || position == HudPosition.TopCenter || position == HudPosition.TopLeft)
        {
            double currentTop = workArea.Top + Margin;
            for (int i = 0; i < slotIndex; i++)
            {
                double h = i < _activeWindows.Count ? GetWindowHeight(_activeWindows[i]) : DefaultSlotHeight;
                currentTop += h + HudSpacing;
            }
            return currentTop;
        }
        else // BottomRight, BottomCenter, BottomLeft
        {
            double hCurrent = slotIndex < _activeWindows.Count ? GetWindowHeight(_activeWindows[slotIndex]) : DefaultSlotHeight;
            double currentBottom = workArea.Bottom - Margin;

            for (int i = 0; i < slotIndex; i++)
            {
                double hPrev = i < _activeWindows.Count ? GetWindowHeight(_activeWindows[i]) : DefaultSlotHeight;
                currentBottom -= (hPrev + HudSpacing);
            }

            return currentBottom - hCurrent;
        }
    }

    private void PositionHudWindow(NotificationHudWindow window, int slotIndex, HudPosition position)
    {
        var workArea = SystemParameters.WorkArea;
        double width = window.Width > 0 ? window.Width : 380.0;
        double left;

        switch (position)
        {
            case HudPosition.TopLeft:
            case HudPosition.BottomLeft:
                left = workArea.Left + Margin;
                break;

            case HudPosition.TopCenter:
            case HudPosition.BottomCenter:
                left = workArea.Left + (workArea.Width - width) / 2.0;
                break;

            case HudPosition.TopRight:
            case HudPosition.BottomRight:
            default:
                left = workArea.Right - width - Margin;
                break;
        }

        window.Left = left;
        window.Top = CalculateSlotTop(slotIndex, position);
    }

    private void RestackWindows(HudPosition position)
    {
        for (int i = 0; i < _activeWindows.Count; i++)
        {
            var win = _activeWindows[i];
            double targetTop = CalculateSlotTop(i, position);

            // Cancel any prior animation on TopProperty to avoid animation property lock
            win.BeginAnimation(Window.TopProperty, null);

            if (Math.Abs(win.Top - targetTop) > 1.0)
            {
                var slideAnim = new DoubleAnimation(win.Top, targetTop, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                };

                slideAnim.Completed += (s, e) =>
                {
                    win.BeginAnimation(Window.TopProperty, null);
                    win.Top = targetTop;
                };

                win.BeginAnimation(Window.TopProperty, slideAnim);
            }
            else
            {
                win.Top = targetTop;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
        catch { }
        DismissAll();
        GC.SuppressFinalize(this);
    }
}

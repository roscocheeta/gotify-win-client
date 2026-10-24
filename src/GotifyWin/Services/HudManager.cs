using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Animation;
using Application = System.Windows.Application;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Views;

namespace GotifyWin.Services;

public interface IHudManager
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

    private readonly List<NotificationHudWindow> _activeWindows = new();
    private readonly Queue<(GotifyMessage Message, PresentationSettings Settings, Action<string>? OnUrlOpen)> _pendingQueue = new();
    private readonly object _lock = new();

    public int ActiveCount
    {
        get { lock (_lock) { return _activeWindows.Count; } }
    }

    public int QueuedCount
    {
        get { lock (_lock) { return _pendingQueue.Count; } }
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
                if (_activeWindows.Count < MaxConcurrentVisible)
                {
                    SpawnHudWindow(message, settings, onUrlOpen);
                }
                else
                {
                    // Queue excess notifications so active banners are not instantly destroyed
                    if (_pendingQueue.Count < 30)
                    {
                        _pendingQueue.Enqueue((message, settings, onUrlOpen));
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

                    // Dequeue next pending notification if available
                    if (_pendingQueue.Count > 0 && _activeWindows.Count < MaxConcurrentVisible)
                    {
                        var next = _pendingQueue.Dequeue();
                        SpawnHudWindow(next.Message, next.Settings, next.OnUrlOpen);
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
}

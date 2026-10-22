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
}

public class HudManager : IHudManager
{
    private const int MaxStackedHuds = 4;
    private const double HudSpacing = 12.0;
    private const double Margin = 20.0;
    private readonly List<NotificationHudWindow> _activeWindows = new();
    private readonly object _lock = new();

    public void ShowNotification(GotifyMessage message, PresentationSettings settings, Action<string>? onUrlOpen)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            lock (_lock)
            {
                // If max stacked reached, close the oldest window to make room
                if (_activeWindows.Count >= MaxStackedHuds)
                {
                    var oldest = _activeWindows[0];
                    _activeWindows.RemoveAt(0);
                    try { oldest.Close(); } catch { }
                }

                var hud = new NotificationHudWindow();
                hud.Setup(message, settings, onUrlOpen);

                _activeWindows.Add(hud);

                // Position the new window
                PositionHudWindow(hud, _activeWindows.Count - 1, settings.Position);

                hud.Closed += (s, e) =>
                {
                    lock (_lock)
                    {
                        if (_activeWindows.Remove(hud))
                        {
                            RestackWindows(settings.Position);
                        }
                    }
                };

                hud.Show();
            }
        });
    }

    private void PositionHudWindow(NotificationHudWindow window, int slotIndex, HudPosition position)
    {
        var workArea = SystemParameters.WorkArea;
        double width = window.Width > 0 ? window.Width : 380.0;
        double height = 130.0; // standard slot height
        double slotOffset = slotIndex * (height + HudSpacing);

        double left;
        double top;

        switch (position)
        {
            case HudPosition.BottomRight:
                left = workArea.Right - width - Margin;
                top = workArea.Bottom - height - Margin - slotOffset;
                break;

            case HudPosition.BottomCenter:
                left = workArea.Left + (workArea.Width - width) / 2.0;
                top = workArea.Bottom - height - Margin - slotOffset;
                break;

            case HudPosition.TopRight:
                left = workArea.Right - width - Margin;
                top = workArea.Top + Margin + slotOffset;
                break;

            case HudPosition.TopCenter:
                left = workArea.Left + (workArea.Width - width) / 2.0;
                top = workArea.Top + Margin + slotOffset;
                break;

            default:
                left = workArea.Right - width - Margin;
                top = workArea.Bottom - height - Margin - slotOffset;
                break;
        }

        window.Left = left;
        window.Top = top;
    }

    private void RestackWindows(HudPosition position)
    {
        for (int i = 0; i < _activeWindows.Count; i++)
        {
            var win = _activeWindows[i];
            var workArea = SystemParameters.WorkArea;
            double height = 130.0;
            double slotOffset = i * (height + HudSpacing);
            double targetTop;

            switch (position)
            {
                case HudPosition.BottomRight:
                case HudPosition.BottomCenter:
                    targetTop = workArea.Bottom - height - Margin - slotOffset;
                    break;

                case HudPosition.TopRight:
                case HudPosition.TopCenter:
                    targetTop = workArea.Top + Margin + slotOffset;
                    break;

                default:
                    targetTop = workArea.Bottom - height - Margin - slotOffset;
                    break;
            }

            // Smooth slide animation to new slot
            var slideAnim = new DoubleAnimation(win.Top, targetTop, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            win.BeginAnimation(Window.TopProperty, slideAnim);
        }
    }
}

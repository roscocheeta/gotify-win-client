using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using BrushConverter = System.Windows.Media.BrushConverter;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;

namespace GotifyWin.Views;

public partial class NotificationHudWindow : Window
{
    private Storyboard? _countdownStoryboard;
    private System.Threading.Timer? _fallbackTimer;
    private double _durationMs = 5000.0;
    private DateTime _expiresAtUtc = DateTime.MaxValue;
    private double _remainingDurationMs;
    private bool _isClosing;
    private bool _isPaused;
    private Action<string>? _onUrlOpen;
    private GotifyMessage? _message;
    private HudPosition _position = HudPosition.BottomRight;

    public Visibility CountdownBarVisibility => CountdownBar.Visibility;
    public double CountdownScaleX => CountdownScaleTransform.ScaleX;
    public Visibility OpenLinkButtonVisibility => OpenLinkButton.Visibility;
    public Visibility DismissButtonVisibility => DismissButton.Visibility;
    public Visibility ActionsGridVisibility => ActionsGrid.Visibility;
    public bool IsExpired => DateTime.UtcNow >= _expiresAtUtc;
    public bool IsClosing => _isClosing;

    public NotificationHudWindow()
    {
        InitializeComponent();

        MouseEnter += (s, e) =>
        {
            if (!_isClosing)
            {
                _isPaused = true;
                _remainingDurationMs = Math.Max(0, (_expiresAtUtc - DateTime.UtcNow).TotalMilliseconds);
                try { _fallbackTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }

                if (_countdownStoryboard != null)
                {
                    try { _countdownStoryboard.Pause(this); } catch { }
                }
            }
        };

        MouseLeave += (s, e) =>
        {
            if (!_isClosing && _isPaused)
            {
                _isPaused = false;
                if (_remainingDurationMs <= 0)
                {
                    CloseWithAnimation();
                    return;
                }

                _expiresAtUtc = DateTime.UtcNow.AddMilliseconds(_remainingDurationMs);

                if (_countdownStoryboard != null)
                {
                    try { _countdownStoryboard.Resume(this); } catch { }
                }

                ArmFallbackTimer(_remainingDurationMs + 200);
            }
        };

        Loaded += NotificationHudWindow_Loaded;
    }

    private void NotificationHudWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (double.IsNaN(Left))
        {
            PositionWindow(_position);
        }

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        RootContainer.BeginAnimation(OpacityProperty, fadeIn);

        StartCountdownAnimation();
    }

    private void StartCountdownAnimation()
    {
        _expiresAtUtc = DateTime.UtcNow.AddMilliseconds(_durationMs);

        _countdownStoryboard = new Storyboard();

        var anim = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(_durationMs)),
            FillBehavior = FillBehavior.HoldEnd
        };

        Storyboard.SetTarget(anim, CountdownScaleTransform);
        Storyboard.SetTargetProperty(anim, new PropertyPath(ScaleTransform.ScaleXProperty));

        _countdownStoryboard.Children.Add(anim);
        _countdownStoryboard.Completed += (s, e) =>
        {
            if (!_isClosing && !_isPaused)
            {
                CloseWithAnimation();
            }
        };

        _countdownStoryboard.Begin(this, isControllable: true);

        // Fallback timer: thread-pool timer guarantees dismissal even if DirectX/DWM composition suspends during lock
        ArmFallbackTimer(_durationMs + 200);
    }

    private void ArmFallbackTimer(double delayMs)
    {
        try
        {
            _fallbackTimer?.Dispose();
            var safeDelay = Math.Max(50, (int)Math.Ceiling(delayMs));
            _fallbackTimer = new System.Threading.Timer(_ =>
            {
                UiDispatcher.RunOnUI(() =>
                {
                    if (!_isClosing && !_isPaused && DateTime.UtcNow >= _expiresAtUtc)
                    {
                        CloseWithAnimation();
                    }
                });
            }, null, safeDelay, Timeout.Infinite);
        }
        catch { }
    }

    public void CheckExpiration()
    {
        if (_isClosing) return;

        if (DateTime.UtcNow >= _expiresAtUtc)
        {
            CloseWithAnimation();
            return;
        }

        if (!_isPaused && _countdownStoryboard != null)
        {
            var remainingMs = (_expiresAtUtc - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs > 0 && remainingMs <= _durationMs)
            {
                ArmFallbackTimer(remainingMs + 200);

                double currentFraction = Math.Clamp(remainingMs / _durationMs, 0.0, 1.0);
                try
                {
                    _countdownStoryboard.Stop(this);
                }
                catch { }

                _countdownStoryboard = new Storyboard();
                var anim = new DoubleAnimation
                {
                    From = currentFraction,
                    To = 0.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(remainingMs)),
                    FillBehavior = FillBehavior.HoldEnd
                };

                Storyboard.SetTarget(anim, CountdownScaleTransform);
                Storyboard.SetTargetProperty(anim, new PropertyPath(ScaleTransform.ScaleXProperty));

                _countdownStoryboard.Children.Add(anim);
                _countdownStoryboard.Completed += (s, e) =>
                {
                    if (!_isClosing && !_isPaused)
                    {
                        CloseWithAnimation();
                    }
                };

                _countdownStoryboard.Begin(this, isControllable: true);
            }
        }
    }

    private async Task LoadNotificationImageAsync(string url)
    {
        try
        {
            var bitmap = await GotifyWin.Services.SecureImageLoader.LoadAsync(url);
            if (bitmap != null && !_isClosing)
            {
                NotificationImage.Source = bitmap;
                NotificationImageBorder.Visibility = Visibility.Visible;
            }
        }
        catch { }
    }

    public void Setup(GotifyMessage message, PresentationSettings settings, Action<string>? onUrlOpen)
    {
        _message = message;
        _onUrlOpen = onUrlOpen;
        _position = settings.Position;

        AppNameTextBlock.Text = message.DisplayAppName;
        TimeTextBlock.Text = message.RelativeTime;
        TitleTextBlock.Text = message.DisplayTitle;
        MessageTextBlock.Text = message.Message;

        PriorityText.Text = message.PriorityDisplay;
        try
        {
            var priorityBrush = new BrushConverter().ConvertFromString(message.PriorityColor) as Brush;
            if (priorityBrush != null)
            {
                PriorityBadge.Background = priorityBrush;
                CountdownBar.Background = priorityBrush;
            }

            var textBrush = new BrushConverter().ConvertFromString(message.PriorityTextColor) as Brush;
            if (textBrush != null)
            {
                PriorityText.Foreground = textBrush;
            }
        }
        catch { }

        // Load Icon
        if (!string.IsNullOrWhiteSpace(message.ApplicationIconPath) && File.Exists(message.ApplicationIconPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(message.ApplicationIconPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                AppIconImage.Source = bitmap;
            }
            catch { }
        }
        else
        {
            try
            {
                var bitmap = new BitmapImage(new Uri("pack://application:,,,/Assets/app-32.png"));
                bitmap.Freeze();
                AppIconImage.Source = bitmap;
            }
            catch { }
        }

        // Load Notification Image (Thumbnail) - secure, origin-restricted, async (never blocks UI)
        if (message.HasImage && GotifyWin.Services.SecureImageLoader.IsAllowed(message.ImageUrl))
        {
            var imageUrl = message.ImageUrl;
            _ = LoadNotificationImageAsync(imageUrl!);
        }

        // Action Link & Dismiss Button
        if (message.HasClickUrl)
        {
            OpenLinkButton.Visibility = Visibility.Visible;
            OpenLinkButton.Content = message.ClickUrlButtonText;
            OpenLinkButton.ToolTip = message.ClickUrl;
        }
        else
        {
            OpenLinkButton.Visibility = Visibility.Collapsed;
        }
        ActionsGrid.Visibility = Visibility.Visible;

        // Countdown Progress Bar visibility
        CountdownBar.Visibility = settings.ShowCountdownBar ? Visibility.Visible : Visibility.Collapsed;

        // Display Duration
        _durationMs = Math.Max(2.0, settings.DisplayDurationSeconds) * 1000.0;
    }

    private void PositionWindow(HudPosition position)
    {
        var workArea = SystemParameters.WorkArea;
        var margin = 20.0;

        Width = 380;
        UpdateLayout();

        var height = ActualHeight > 0 ? ActualHeight : 140.0;

        double left = workArea.Right - Width - margin;
        double top = workArea.Bottom - height - margin;

        switch (position)
        {
            case HudPosition.TopLeft:
                left = workArea.Left + margin;
                top = workArea.Top + margin;
                break;
            case HudPosition.BottomLeft:
                left = workArea.Left + margin;
                top = workArea.Bottom - height - margin;
                break;
            case HudPosition.BottomRight:
                left = workArea.Right - Width - margin;
                top = workArea.Bottom - height - margin;
                break;
            case HudPosition.TopRight:
                left = workArea.Right - Width - margin;
                top = workArea.Top + margin;
                break;
            case HudPosition.TopCenter:
                left = workArea.Left + (workArea.Width - Width) / 2.0;
                top = workArea.Top + margin;
                break;
            case HudPosition.BottomCenter:
                left = workArea.Left + (workArea.Width - Width) / 2.0;
                top = workArea.Bottom - height - margin;
                break;
        }

        Left = left;
        Top = top;
    }

    public void CloseWithAnimation()
    {
        if (_isClosing) return;
        _isClosing = true;

        try
        {
            _fallbackTimer?.Dispose();
            _fallbackTimer = null;
            _countdownStoryboard?.Stop(this);
        }
        catch { }

        bool closed = false;
        void SafeClose()
        {
            if (closed) return;
            closed = true;
            try { Close(); } catch { }
        }

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (s, e) => SafeClose();

        RootContainer.BeginAnimation(OpacityProperty, fadeOut);

        // Safety fallback: if WPF composition engine is suspended (e.g. workstation lock),
        // ensure Close() is still guaranteed to execute.
        _ = Task.Delay(350).ContinueWith(_ =>
        {
            UiDispatcher.RunOnUI(SafeClose);
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _fallbackTimer?.Dispose();
            _fallbackTimer = null;
            _countdownStoryboard?.Stop(this);
            _countdownStoryboard = null;
        }
        catch { }
        base.OnClosed(e);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseWithAnimation();
    }

    private void OpenLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_message?.ClickUrl != null)
        {
            _onUrlOpen?.Invoke(_message.ClickUrl);
        }
        CloseWithAnimation();
    }
}

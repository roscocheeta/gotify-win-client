using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using BrushConverter = System.Windows.Media.BrushConverter;
using GotifyWin.Models;
using GotifyWin.Models.Config;

namespace GotifyWin.Views;

public partial class NotificationHudWindow : Window
{
    private Storyboard? _countdownStoryboard;
    private double _durationMs = 5000.0;
    private bool _isClosing;
    private Action<string>? _onUrlOpen;
    private GotifyMessage? _message;
    private HudPosition _position = HudPosition.BottomRight;

    public Visibility CountdownBarVisibility => CountdownBar.Visibility;
    public double CountdownScaleX => CountdownScaleTransform.ScaleX;

    public NotificationHudWindow()
    {
        InitializeComponent();

        MouseEnter += (s, e) =>
        {
            if (!_isClosing && _countdownStoryboard != null)
            {
                try { _countdownStoryboard.Pause(this); } catch { }
            }
        };

        MouseLeave += (s, e) =>
        {
            if (!_isClosing && _countdownStoryboard != null)
            {
                try { _countdownStoryboard.Resume(this); } catch { }
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
            if (!_isClosing)
            {
                CloseWithAnimation();
            }
        };

        _countdownStoryboard.Begin(this, isControllable: true);
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

        // Action Link
        if (message.HasClickUrl)
        {
            ActionsGrid.Visibility = Visibility.Visible;
            OpenLinkButton.Content = message.ClickUrlButtonText;
            OpenLinkButton.ToolTip = message.ClickUrl;
        }

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

    private void CloseWithAnimation()
    {
        if (_isClosing) return;
        _isClosing = true;

        try
        {
            _countdownStoryboard?.Stop(this);
        }
        catch { }

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (s, e) =>
        {
            try { Close(); } catch { }
        };

        RootContainer.BeginAnimation(OpacityProperty, fadeOut);
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
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

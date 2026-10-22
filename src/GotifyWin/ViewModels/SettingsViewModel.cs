using System.Windows.Input;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;

namespace GotifyWin.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IGotifyApiService _apiService;
    private readonly IStartupManager _startupManager;
    private readonly INotificationPresenter _presenter;
    private readonly IThemeManager? _themeManager;

    // Connection
    private string _serverUrl = string.Empty;
    public string ServerUrl
    {
        get => _serverUrl;
        set => SetProperty(ref _serverUrl, value);
    }

    private string _clientToken = string.Empty;
    public string ClientToken
    {
        get => _clientToken;
        set
        {
            if (SetProperty(ref _clientToken, value))
            {
                OnPropertyChanged(nameof(MaskedDisplayToken));
            }
        }
    }

    public string MaskedDisplayToken => string.IsNullOrEmpty(_clientToken) ? "(No token set)" : new string('●', Math.Min(32, _clientToken.Length));

    private bool _isTokenRevealed;
    public bool IsTokenRevealed
    {
        get => _isTokenRevealed;
        set
        {
            if (SetProperty(ref _isTokenRevealed, value))
            {
                OnPropertyChanged(nameof(TokenEyeIcon));
                OnPropertyChanged(nameof(TokenEyeTooltip));
            }
        }
    }

    public string TokenEyeIcon => IsTokenRevealed ? "🙈" : "👁";
    public string TokenEyeTooltip => IsTokenRevealed ? "Hide Token" : "Reveal Token";

    private string _testStatus = string.Empty;
    public string TestStatus
    {
        get => _testStatus;
        set => SetProperty(ref _testStatus, value);
    }

    private bool _isTesting;
    public bool IsTesting
    {
        get => _isTesting;
        set => SetProperty(ref _isTesting, value);
    }

    // Presentation
    public NotificationMode SelectedMode
    {
        get => _configService.Config.Presentation.Mode;
        set
        {
            if (_configService.Config.Presentation.Mode != value)
            {
                _configService.Config.Presentation.Mode = value;
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    public HudPosition SelectedPosition
    {
        get => _configService.Config.Presentation.Position;
        set
        {
            if (_configService.Config.Presentation.Position != value)
            {
                _configService.Config.Presentation.Position = value;
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    public double DisplayDurationSeconds
    {
        get => _configService.Config.Presentation.DisplayDurationSeconds;
        set
        {
            if (Math.Abs(_configService.Config.Presentation.DisplayDurationSeconds - value) > 0.01)
            {
                _configService.Config.Presentation.DisplayDurationSeconds = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DurationText));
                _configService.Save();
            }
        }
    }

    public string DurationText => $"{DisplayDurationSeconds:0} seconds";

    public bool ShowCountdownBar
    {
        get => _configService.Config.Presentation.ShowCountdownBar;
        set
        {
            if (_configService.Config.Presentation.ShowCountdownBar != value)
            {
                _configService.Config.Presentation.ShowCountdownBar = value;
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    public bool PlaySound
    {
        get => _configService.Config.Presentation.PlaySound;
        set
        {
            if (_configService.Config.Presentation.PlaySound != value)
            {
                _configService.Config.Presentation.PlaySound = value;
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    public bool EnableDebounce
    {
        get => _configService.Config.Presentation.EnableDebounce;
        set
        {
            if (_configService.Config.Presentation.EnableDebounce != value)
            {
                _configService.Config.Presentation.EnableDebounce = value;
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    public int DebounceWindowSeconds
    {
        get => _configService.Config.Presentation.DebounceWindowSeconds;
        set
        {
            if (_configService.Config.Presentation.DebounceWindowSeconds != value)
            {
                _configService.Config.Presentation.DebounceWindowSeconds = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DebounceText));
                _configService.Save();
            }
        }
    }

    public string DebounceText => $"{DebounceWindowSeconds}s window";

    // DND
    public bool EnableEmergencyBypass
    {
        get => _configService.Config.Dnd.EnableEmergencyBypass;
        set
        {
            if (_configService.Config.Dnd.EnableEmergencyBypass != value)
            {
                _configService.Config.Dnd.EnableEmergencyBypass = value;
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    public int EmergencyBypassPriority
    {
        get => _configService.Config.Dnd.EmergencyBypassPriority;
        set
        {
            if (_configService.Config.Dnd.EmergencyBypassPriority != value)
            {
                _configService.Config.Dnd.EmergencyBypassPriority = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BypassText));
                OnPropertyChanged(nameof(EmergencyBypassPriorityText));
                _configService.Save();
            }
        }
    }

    public string EmergencyBypassPriorityText => $"Priority \u2265 {EmergencyBypassPriority} ({GetPriorityLabel(EmergencyBypassPriority)})";
    public string BypassText => EmergencyBypassPriorityText;

    private static string GetPriorityLabel(int p) => p switch
    {
        <= 4 => "Low",
        5 or 6 => "Normal",
        7 => "High",
        8 => "Urgent",
        _ => "Critical"
    };

    public bool QuietHoursEnabled
    {
        get => _configService.Config.Dnd.QuietHoursEnabled;
        set
        {
            if (_configService.Config.Dnd.QuietHoursEnabled != value)
            {
                _configService.Config.Dnd.QuietHoursEnabled = value;
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    public string QuietHoursStart
    {
        get => _configService.Config.Dnd.QuietHoursStart.ToString(@"hh\:mm");
        set
        {
            if (TimeSpan.TryParse(value, out var parsed))
            {
                _configService.Config.Dnd.QuietHoursStart = parsed;
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    public string QuietHoursEnd
    {
        get => _configService.Config.Dnd.QuietHoursEnd.ToString(@"hh\:mm");
        set
        {
            if (TimeSpan.TryParse(value, out var parsed))
            {
                _configService.Config.Dnd.QuietHoursEnd = parsed;
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    public bool SuppressOnFullscreen
    {
        get => _configService.Config.Dnd.SuppressOnFullscreenApps;
        set
        {
            if (_configService.Config.Dnd.SuppressOnFullscreenApps != value)
            {
                _configService.Config.Dnd.SuppressOnFullscreenApps = value;
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    // System
    public bool StartWithWindows
    {
        get => _configService.Config.StartWithWindows;
        set
        {
            if (_configService.Config.StartWithWindows != value)
            {
                _configService.Config.StartWithWindows = value;
                _startupManager.SetAutoStart(value);
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _configService.Config.MinimizeToTrayOnClose;
        set
        {
            if (_configService.Config.MinimizeToTrayOnClose != value)
            {
                _configService.Config.MinimizeToTrayOnClose = value;
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    public AppThemeMode SelectedTheme
    {
        get => _configService.Config.Theme;
        set
        {
            if (_configService.Config.Theme != value)
            {
                _configService.Config.Theme = value;
                OnPropertyChanged();
                _configService.Save();
                _themeManager?.ApplyTheme(value);
            }
        }
    }

    public ICommand TestConnectionCommand { get; }
    public ICommand SaveConnectionCommand { get; }
    public ICommand SendTestNotificationCommand { get; }
    public ICommand ToggleTokenVisibilityCommand { get; }
    public ICommand CopyTokenCommand { get; }

    public event EventHandler? ConnectionConfigSaved;

    public SettingsViewModel(
        IConfigService configService,
        IGotifyApiService apiService,
        IStartupManager startupManager,
        INotificationPresenter presenter,
        IThemeManager? themeManager = null)
    {
        _configService = configService;
        _apiService = apiService;
        _startupManager = startupManager;
        _presenter = presenter;
        _themeManager = themeManager;

        ServerUrl = _configService.Config.ServerUrl;
        ClientToken = _configService.Config.ClientToken;

        TestConnectionCommand = new RelayCommand(async () => await TestConnectionAsync());
        SaveConnectionCommand = new RelayCommand(SaveConnection);
        SendTestNotificationCommand = new RelayCommand(SendTestNotification);
        ToggleTokenVisibilityCommand = new RelayCommand(() => IsTokenRevealed = !IsTokenRevealed);
        CopyTokenCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(ClientToken))
            {
                try { System.Windows.Clipboard.SetText(ClientToken); } catch { }
            }
        });
    }

    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(ClientToken))
        {
            TestStatus = "Please provide both Server URL and Client Token.";
            return;
        }

        IsTesting = true;
        TestStatus = "Testing connection...";

        try
        {
            var user = await _apiService.GetCurrentUserAsync(ServerUrl, ClientToken);
            if (user != null)
            {
                TestStatus = $"Connected! Authenticated as: {user.Name}";
                // Automatically save connection and trigger WebSocket reconnect on successful test
                SaveConnection();
            }
            else
            {
                TestStatus = "Authentication failed. Check your token and server URL.";
            }
        }
        catch (Exception ex)
        {
            TestStatus = $"Connection error: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private void SaveConnection()
    {
        _configService.Config.ServerUrl = ServerUrl.Trim().TrimEnd('/');
        _configService.Config.ClientToken = ClientToken.Trim();
        _configService.Save();

        TestStatus = "Settings saved successfully.";
        ConnectionConfigSaved?.Invoke(this, EventArgs.Empty);
    }

    private void SendTestNotification()
    {
        var testMsg = new GotifyMessage
        {
            Id = 9999,
            AppId = 1,
            Title = "Gotify Test Alert",
            Message = "This is a preview of your Windows 11 notification style & sound settings.",
            Priority = 6,
            Date = DateTime.UtcNow,
            ApplicationName = "Gotify Client",
            Extras = new Dictionary<string, System.Text.Json.JsonElement>()
        };

        _presenter.Present(testMsg, _configService.Config);
    }
}

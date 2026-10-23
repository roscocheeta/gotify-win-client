using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using Microsoft.Win32;

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
        set
        {
            if (SetProperty(ref _serverUrl, value))
            {
                OnPropertyChanged(nameof(ConnectionSummaryText));
            }
        }
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
        set
        {
            if (SetProperty(ref _testStatus, value))
            {
                OnPropertyChanged(nameof(ConnectionSummaryText));
            }
        }
    }

    private bool _isTesting;
    public bool IsTesting
    {
        get => _isTesting;
        set => SetProperty(ref _isTesting, value);
    }

    // Collapsible Categories
    private bool _isConnectionExpanded = true;
    public bool IsConnectionExpanded
    {
        get => _isConnectionExpanded;
        set => SetProperty(ref _isConnectionExpanded, value);
    }

    private bool _isNotificationsExpanded = true;
    public bool IsNotificationsExpanded
    {
        get => _isNotificationsExpanded;
        set => SetProperty(ref _isNotificationsExpanded, value);
    }

    private bool _isFilteringExpanded;
    public bool IsFilteringExpanded
    {
        get => _isFilteringExpanded;
        set => SetProperty(ref _isFilteringExpanded, value);
    }

    private bool _isDndExpanded;
    public bool IsDndExpanded
    {
        get => _isDndExpanded;
        set => SetProperty(ref _isDndExpanded, value);
    }

    private bool _isAppearanceExpanded;
    public bool IsAppearanceExpanded
    {
        get => _isAppearanceExpanded;
        set => SetProperty(ref _isAppearanceExpanded, value);
    }

    private bool _isBackupExpanded;
    public bool IsBackupExpanded
    {
        get => _isBackupExpanded;
        set => SetProperty(ref _isBackupExpanded, value);
    }

    public string ConnectionSummaryText => string.IsNullOrWhiteSpace(ServerUrl)
        ? "Not Configured"
        : (TestStatus.StartsWith("Connected") ? "● Connected" : ServerUrl);

    public string NotificationsSummaryText => $"{SelectedModeDisplay} • {SelectedPositionDisplay} • {DisplayDurationSeconds:0}s";

    public string SelectedModeDisplay => SelectedMode switch
    {
        NotificationMode.CustomHud => "Floating Banner",
        NotificationMode.NativeToast => "Windows Center",
        NotificationMode.Both => "Both Modes",
        _ => SelectedMode.ToString()
    };

    public string SelectedPositionDisplay => SelectedPosition switch
    {
        HudPosition.BottomRight => "Bottom-Right",
        HudPosition.TopRight => "Top-Right",
        HudPosition.TopLeft => "Top-Left",
        HudPosition.BottomLeft => "Bottom-Left",
        HudPosition.TopCenter => "Top-Center",
        HudPosition.BottomCenter => "Bottom-Center",
        _ => SelectedPosition.ToString()
    };

    public string FilteringSummaryText => $"{(EnableDebounce ? $"Debounce {DebounceWindowSeconds}s" : "Debounce Off")} • {GlobalMinPriorityText}";

    public string DndSummaryText => $"{(QuietHoursEnabled ? $"Quiet Hours ({QuietHoursStart}-{QuietHoursEnd})" : "Quiet Hours Off")}{(EnableEmergencyBypass ? $" • Bypass \u2265 {EmergencyBypassPriority}" : "")}";

    public string AppearanceSummaryText => $"{SelectedTheme} • {(StartWithWindows ? "Auto-start" : "Manual")}";

    public string BackupSummaryText => "Export / Import Configuration";

    public ICommand ToggleConnectionCommand { get; }
    public ICommand ToggleNotificationsCommand { get; }
    public ICommand ToggleFilteringCommand { get; }
    public ICommand ToggleDndCommand { get; }
    public ICommand ToggleAppearanceCommand { get; }
    public ICommand ToggleBackupCommand { get; }

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
                OnPropertyChanged(nameof(NotificationsSummaryText));
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
                OnPropertyChanged(nameof(NotificationsSummaryText));
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
                OnPropertyChanged(nameof(DisplayDurationText));
                OnPropertyChanged(nameof(NotificationsSummaryText));
                _configService.Save();
            }
        }
    }

    public string DurationText => $"{DisplayDurationSeconds:0} seconds";
    public string DisplayDurationText => DurationText;

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

    public int GlobalMinPriority
    {
        get => _configService.Config.GlobalMinPriority;
        set
        {
            if (_configService.Config.GlobalMinPriority != value)
            {
                _configService.Config.GlobalMinPriority = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GlobalMinPriorityText));
                OnPropertyChanged(nameof(FilteringSummaryText));
                _configService.Save();
            }
        }
    }

    public string GlobalMinPriorityText => GlobalMinPriority == 0
        ? "All Messages (0+)"
        : $"Priority \u2265 {GlobalMinPriority} ({GetPriorityLabel(GlobalMinPriority)})";

    public bool EnableDebounce
    {
        get => _configService.Config.Presentation.EnableDebounce;
        set
        {
            if (_configService.Config.Presentation.EnableDebounce != value)
            {
                _configService.Config.Presentation.EnableDebounce = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilteringSummaryText));
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
                OnPropertyChanged(nameof(FilteringSummaryText));
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
                OnPropertyChanged(nameof(DndSummaryText));
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
                OnPropertyChanged(nameof(DndSummaryText));
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
                OnPropertyChanged(nameof(DndSummaryText));
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
                OnPropertyChanged(nameof(DndSummaryText));
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
                OnPropertyChanged(nameof(DndSummaryText));
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
                OnPropertyChanged(nameof(AppearanceSummaryText));
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
                OnPropertyChanged(nameof(AppearanceSummaryText));
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
                OnPropertyChanged(nameof(AppearanceSummaryText));
                _configService.Save();
                _themeManager?.ApplyTheme(value);
            }
        }
    }

    private string _backupStatusText = string.Empty;
    public string BackupStatusText
    {
        get => _backupStatusText;
        set
        {
            if (SetProperty(ref _backupStatusText, value))
            {
                OnPropertyChanged(nameof(HasBackupStatus));
            }
        }
    }
    public bool HasBackupStatus => !string.IsNullOrEmpty(BackupStatusText);

    public ICommand TestConnectionCommand { get; }
    public ICommand SaveConnectionCommand { get; }
    public ICommand SendTestNotificationCommand { get; }
    public ICommand ToggleTokenVisibilityCommand { get; }
    public ICommand CopyTokenCommand { get; }
    public ICommand ExportSettingsCommand { get; }
    public ICommand ImportSettingsCommand { get; }

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
        ExportSettingsCommand = new RelayCommand(ExportSettings);
        ImportSettingsCommand = new RelayCommand(ImportSettings);

        ToggleConnectionCommand = new RelayCommand(() => IsConnectionExpanded = !IsConnectionExpanded);
        ToggleNotificationsCommand = new RelayCommand(() => IsNotificationsExpanded = !IsNotificationsExpanded);
        ToggleFilteringCommand = new RelayCommand(() => IsFilteringExpanded = !IsFilteringExpanded);
        ToggleDndCommand = new RelayCommand(() => IsDndExpanded = !IsDndExpanded);
        ToggleAppearanceCommand = new RelayCommand(() => IsAppearanceExpanded = !IsAppearanceExpanded);
        ToggleBackupCommand = new RelayCommand(() => IsBackupExpanded = !IsBackupExpanded);
    }

    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(ClientToken))
        {
            TestStatus = "Please provide both Server URL and Client Token.";
            return;
        }

        var urlCheck = ServerUrlValidator.Validate(ServerUrl);
        if (!urlCheck.IsValid)
        {
            TestStatus = urlCheck.Error!;
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
                if (urlCheck.Warning != null)
                {
                    TestStatus = $"Settings saved. ⚠️ {urlCheck.Warning}";
                }
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
            Title = "Gotify Test Message",
            Message = "This is a preview of your Windows 11 notification style & sound settings.",
            Priority = 6,
            Date = DateTime.UtcNow,
            ApplicationName = "Gotify Client",
            Extras = new Dictionary<string, System.Text.Json.JsonElement>()
        };

        _presenter.Present(testMsg, _configService.Config);
    }

    public static GotifyClientConfig BuildExportConfig(GotifyClientConfig source)
    {
        var clone = JsonSerializer.Deserialize<GotifyClientConfig>(JsonSerializer.Serialize(source)) ?? new GotifyClientConfig();
        clone.ClientToken = string.Empty;
        return clone;
    }

    public void ExportSettings()
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Settings Backup",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                FileName = $"gotify-settings-{DateTime.Now:yyyyMMdd-HHmm}.json"
            };

            if (dlg.ShowDialog() == true)
            {
                _configService.Config.ServerUrl = ServerUrl.Trim().TrimEnd('/');
                _configService.Config.ClientToken = ClientToken.Trim();

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                };

                // SEC: Never write the client token to backup files; re-enter it after restoring on a new machine.
                var json = JsonSerializer.Serialize(BuildExportConfig(_configService.Config), options);
                File.WriteAllText(dlg.FileName, json);
                BackupStatusText = $"✓ Settings exported to {Path.GetFileName(dlg.FileName)} (client token not included)";
            }
        }
        catch (Exception ex)
        {
            BackupStatusText = $"Export error: {ex.Message}";
        }
    }

    public void ImportSettings()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Settings Backup",
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                var json = File.ReadAllText(dlg.FileName);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() }
                };

                var loaded = JsonSerializer.Deserialize<GotifyClientConfig>(json, options);
                if (loaded == null)
                {
                    BackupStatusText = "⚠️ Invalid settings file.";
                    return;
                }

                // SEC: Only accept a valid http(s) server URL; keep existing connection values if the file has none.
                if (ServerUrlValidator.Validate(loaded.ServerUrl).IsValid)
                {
                    _configService.Config.ServerUrl = loaded.ServerUrl!.Trim().TrimEnd('/');
                }
                if (!string.IsNullOrWhiteSpace(loaded.ClientToken))
                {
                    _configService.Config.ClientToken = loaded.ClientToken;
                }
                _configService.Config.Theme = loaded.Theme;
                _configService.Config.GlobalMinPriority = loaded.GlobalMinPriority;
                _configService.Config.StartWithWindows = loaded.StartWithWindows;
                _configService.Config.MinimizeToTrayOnClose = loaded.MinimizeToTrayOnClose;
                _configService.Config.StartMinimized = loaded.StartMinimized;
                _configService.Config.MaxHistoryCount = loaded.MaxHistoryCount;

                if (loaded.Presentation != null)
                {
                    _configService.Config.Presentation.Mode = loaded.Presentation.Mode;
                    _configService.Config.Presentation.Position = loaded.Presentation.Position;
                    _configService.Config.Presentation.DisplayDurationSeconds = loaded.Presentation.DisplayDurationSeconds;
                    _configService.Config.Presentation.ShowCountdownBar = loaded.Presentation.ShowCountdownBar;
                    _configService.Config.Presentation.PlaySound = loaded.Presentation.PlaySound;
                    _configService.Config.Presentation.EnableDebounce = loaded.Presentation.EnableDebounce;
                    _configService.Config.Presentation.DebounceWindowSeconds = loaded.Presentation.DebounceWindowSeconds;
                    _configService.Config.Presentation.PauseOnHover = loaded.Presentation.PauseOnHover;
                    _configService.Config.Presentation.Width = loaded.Presentation.Width;
                    _configService.Config.Presentation.MaxBodyLines = loaded.Presentation.MaxBodyLines;
                }

                if (loaded.Dnd != null)
                {
                    _configService.Config.Dnd.EmergencyBypassPriority = loaded.Dnd.EmergencyBypassPriority;
                    _configService.Config.Dnd.EnableEmergencyBypass = loaded.Dnd.EnableEmergencyBypass;
                    _configService.Config.Dnd.QuietHoursEnabled = loaded.Dnd.QuietHoursEnabled;
                    _configService.Config.Dnd.QuietHoursStart = loaded.Dnd.QuietHoursStart;
                    _configService.Config.Dnd.QuietHoursEnd = loaded.Dnd.QuietHoursEnd;
                    _configService.Config.Dnd.SuppressOnFullscreenApps = loaded.Dnd.SuppressOnFullscreenApps;
                }

                if (loaded.AppFilters != null)
                {
                    _configService.Config.AppFilters = loaded.AppFilters;
                }

                if (loaded.Rules != null && loaded.Rules.Count > 0)
                {
                    _configService.Config.Rules = loaded.Rules;
                }

                _configService.Save();

                ServerUrl = _configService.Config.ServerUrl;
                ClientToken = _configService.Config.ClientToken;
                OnPropertyChanged(nameof(SelectedMode));
                OnPropertyChanged(nameof(SelectedPosition));
                OnPropertyChanged(nameof(DisplayDurationSeconds));
                OnPropertyChanged(nameof(DisplayDurationText));
                OnPropertyChanged(nameof(ShowCountdownBar));
                OnPropertyChanged(nameof(PlaySound));
                OnPropertyChanged(nameof(SuppressOnFullscreen));
                OnPropertyChanged(nameof(GlobalMinPriority));
                OnPropertyChanged(nameof(GlobalMinPriorityText));
                OnPropertyChanged(nameof(EnableDebounce));
                OnPropertyChanged(nameof(DebounceWindowSeconds));
                OnPropertyChanged(nameof(DebounceText));
                OnPropertyChanged(nameof(EnableEmergencyBypass));
                OnPropertyChanged(nameof(EmergencyBypassPriority));
                OnPropertyChanged(nameof(BypassText));
                OnPropertyChanged(nameof(QuietHoursEnabled));
                OnPropertyChanged(nameof(QuietHoursStart));
                OnPropertyChanged(nameof(QuietHoursEnd));
                OnPropertyChanged(nameof(SelectedTheme));
                OnPropertyChanged(nameof(StartWithWindows));
                OnPropertyChanged(nameof(MinimizeToTrayOnClose));

                _themeManager?.ApplyTheme(_configService.Config.Theme);
                _startupManager.SetAutoStart(_configService.Config.StartWithWindows);

                BackupStatusText = $"✓ Settings successfully imported from {Path.GetFileName(dlg.FileName)}";
                ConnectionConfigSaved?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            BackupStatusText = $"Import error: {ex.Message}";
        }
    }
}

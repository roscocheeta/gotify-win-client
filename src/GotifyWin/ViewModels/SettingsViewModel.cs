using System.Net.Http;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
    private readonly IUpdateService _updateService;

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

    // Collapsible Categories - start with all categories collapsed
    private bool _isConnectionExpanded = false;
    public bool IsConnectionExpanded
    {
        get => _isConnectionExpanded;
        set => SetProperty(ref _isConnectionExpanded, value);
    }

    private bool _isNotificationsExpanded = false;
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

    private bool _isAboutExpanded;
    public bool IsAboutExpanded
    {
        get => _isAboutExpanded;
        set => SetProperty(ref _isAboutExpanded, value);
    }

    public string ConnectionSummaryText => string.IsNullOrWhiteSpace(ServerUrl)
        ? "Not Configured"
        : (TestStatus.StartsWith("Connected") ? "● Connected" : ServerUrl);

    public string NotificationsSummaryText =>
        SelectedMode == NotificationMode.NativeToast
            ? $"{SelectedModeDisplay} • System Managed"
            : $"{SelectedModeDisplay} • {SelectedPositionDisplay} • {DisplayDurationSeconds:0}s";

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

    public string AppearanceSummaryText => $"{SelectedTheme} • {(UseCompactFeedView ? "Compact Feed" : "Detailed Feed")} • {(StartWithWindows ? "Auto-start" : "Manual")}";

    public string BackupSummaryText => "Export / Import Configuration";

    public string AboutSummaryText => IsUpdateAvailable ? $"● Update Available ({LatestAppVersion})" : $"{CurrentAppVersion} • Up to date";

    public ICommand ToggleConnectionCommand { get; }
    public ICommand ToggleNotificationsCommand { get; }
    public ICommand ToggleFilteringCommand { get; }
    public ICommand ToggleDndCommand { get; }
    public ICommand ToggleAppearanceCommand { get; }
    public ICommand ToggleBackupCommand { get; }
    public ICommand ToggleAboutCommand { get; }

    // Presentation
    public bool IsHudCustomizationEnabled => SelectedMode == NotificationMode.CustomHud || SelectedMode == NotificationMode.Both;

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
                OnPropertyChanged(nameof(IsHudCustomizationEnabled));
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

    public bool AllowExternalImages
    {
        get => _configService.Config.Presentation.AllowExternalImages;
        set
        {
            if (_configService.Config.Presentation.AllowExternalImages != value)
            {
                _configService.Config.Presentation.AllowExternalImages = value;
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

    public bool RetainGameDndOnAltTab
    {
        get => _configService.Config.Dnd.RetainGameDndOnAltTab;
        set
        {
            if (_configService.Config.Dnd.RetainGameDndOnAltTab != value)
            {
                _configService.Config.Dnd.RetainGameDndOnAltTab = value;
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

    public bool UseCompactFeedView
    {
        get => _configService.Config.UseCompactFeedView;
        set
        {
            if (_configService.Config.UseCompactFeedView != value)
            {
                _configService.Config.UseCompactFeedView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AppearanceSummaryText));
                _configService.Save();
            }
        }
    }

    public bool HideFilteredInFeed
    {
        get => _configService.Config.HideFilteredInFeed;
        set
        {
            if (_configService.Config.HideFilteredInFeed != value)
            {
                _configService.Config.HideFilteredInFeed = value;
                OnPropertyChanged();
                _configService.Save();
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

    // Updates & Version Info
    public string CurrentAppVersion => _updateService.CurrentVersionString;

    private string _latestAppVersion = string.Empty;
    public string LatestAppVersion
    {
        get => _latestAppVersion;
        set
        {
            if (SetProperty(ref _latestAppVersion, value))
            {
                OnPropertyChanged(nameof(AboutSummaryText));
            }
        }
    }

    private string _latestReleaseName = string.Empty;
    public string LatestReleaseName
    {
        get => _latestReleaseName;
        set => SetProperty(ref _latestReleaseName, value);
    }

    private string _releaseNotes = string.Empty;
    public string ReleaseNotes
    {
        get => _releaseNotes;
        set => SetProperty(ref _releaseNotes, value);
    }

    private string _releasePageUrl = string.Empty;
    public string ReleasePageUrl
    {
        get => _releasePageUrl;
        set => SetProperty(ref _releasePageUrl, value);
    }

    private DateTime? _publishedAt;
    public string PublishedDateText => _publishedAt.HasValue ? _publishedAt.Value.ToLocalTime().ToString("MMM dd, yyyy") : string.Empty;

    private bool _isUpdateAvailable;
    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set
        {
            if (SetProperty(ref _isUpdateAvailable, value))
            {
                OnPropertyChanged(nameof(AboutSummaryText));
            }
        }
    }

    private bool _isCheckingForUpdates;
    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        set => SetProperty(ref _isCheckingForUpdates, value);
    }

    private bool _isDownloadingUpdate;
    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        set => SetProperty(ref _isDownloadingUpdate, value);
    }

    private bool _isUpdateReadyToInstall;
    public bool IsUpdateReadyToInstall
    {
        get => _isUpdateReadyToInstall;
        set => SetProperty(ref _isUpdateReadyToInstall, value);
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, value);
    }

    private string _updateStatusMessage = string.Empty;
    public string UpdateStatusMessage
    {
        get => _updateStatusMessage;
        set => SetProperty(ref _updateStatusMessage, value);
    }

    public bool AutoCheckForUpdates
    {
        get => _configService.Config.AutoCheckForUpdates;
        set
        {
            if (_configService.Config.AutoCheckForUpdates != value)
            {
                _configService.Config.AutoCheckForUpdates = value;
                OnPropertyChanged();
                _configService.Save();
            }
        }
    }

    private UpdateCheckResult? _lastCheckResult;
    private string? _downloadedZipPath;

    public ICommand TestConnectionCommand { get; }
    public ICommand SaveConnectionCommand { get; }
    public ICommand SendTestNotificationCommand { get; }
    public ICommand ToggleTokenVisibilityCommand { get; }
    public ICommand CopyTokenCommand { get; }
    public ICommand ExportSettingsCommand { get; }
    public ICommand ImportSettingsCommand { get; }

    public ICommand CheckForUpdatesCommand { get; }
    public ICommand DownloadAndInstallUpdateCommand { get; }
    public ICommand ApplyUpdateAndRestartCommand { get; }
    public ICommand OpenReleaseNotesCommand { get; }
    public ICommand OpenProjectRepoCommand { get; }

    public event EventHandler? ConnectionConfigSaved;
    public event Action? RequestScrollToAbout;

    public void ScrollToAbout()
    {
        IsAboutExpanded = true;
        RequestScrollToAbout?.Invoke();
    }

    public SettingsViewModel(
        IConfigService configService,
        IGotifyApiService apiService,
        IStartupManager startupManager,
        INotificationPresenter presenter,
        IThemeManager? themeManager = null,
        IUpdateService? updateService = null)
    {
        _configService = configService;
        _apiService = apiService;
        _startupManager = startupManager;
        _presenter = presenter;
        _themeManager = themeManager;
        _updateService = updateService ?? new UpdateService();
        _updateService.UpdateAvailable += OnUpdateDiscovered;

        ServerUrl = _configService.Config.ServerUrl;
        ClientToken = _configService.Config.ClientToken;

        if (_configService.HasDecryptionFailed)
        {
            TestStatus = "⚠️ Stored token could not be decrypted (profile change). Please re-enter your client token.";
        }

        TestConnectionCommand = new RelayCommand(async () => await TestConnectionAsync());
        SaveConnectionCommand = new RelayCommand(SaveConnection);
        SendTestNotificationCommand = new RelayCommand(SendTestNotification);
        ToggleTokenVisibilityCommand = new RelayCommand(() => IsTokenRevealed = !IsTokenRevealed);
        CopyTokenCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(ClientToken))
            {
                try
                {
                    System.Windows.Clipboard.SetText(ClientToken);
                    TestStatus = "✓ Token copied to clipboard (will auto-clear in 30s).";
                    ScheduleClipboardClear(ClientToken);
                }
                catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
                {
                    Debug.WriteLine($"Failed to set clipboard: {ex.Message}");
                }
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
        ToggleAboutCommand = new RelayCommand(() => IsAboutExpanded = !IsAboutExpanded);

        CheckForUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesAsync(isManual: true));
        DownloadAndInstallUpdateCommand = new RelayCommand(async () => await DownloadAndInstallUpdateAsync());
        ApplyUpdateAndRestartCommand = new RelayCommand(async () => await ApplyUpdateAndRestartAsync());
        OpenReleaseNotesCommand = new RelayCommand(() => OpenUrl(ReleasePageUrl));
        OpenProjectRepoCommand = new RelayCommand(() => OpenUrl("https://github.com/roscocheeta/gotify-win-client"));
    }

    private void OnUpdateDiscovered(UpdateCheckResult result)
    {
        UiDispatcher.RunOnUI(() =>
        {
            _lastCheckResult = result;
            IsUpdateAvailable = result.IsUpdateAvailable;
            LatestAppVersion = result.LatestVersionTag;
            LatestReleaseName = result.ReleaseName;
            ReleaseNotes = result.ReleaseNotes;
            ReleasePageUrl = result.ReleasePageUrl;
            _publishedAt = result.PublishedAt;
            OnPropertyChanged(nameof(PublishedDateText));
            UpdateStatusMessage = $"Update {result.LatestVersionTag} is available!";
        });
    }

    public async Task CheckForUpdatesAsync(bool isManual = true)
    {
        if (IsCheckingForUpdates || IsDownloadingUpdate) return;

        IsCheckingForUpdates = true;
        UpdateStatusMessage = "Checking GitHub for updates...";

        try
        {
            var result = await _updateService.CheckForUpdatesAsync(isManual);
            _lastCheckResult = result;
            _configService.Config.LastUpdateCheckUtc = DateTime.UtcNow;
            _configService.Save();

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                UpdateStatusMessage = $"Update check failed: {result.ErrorMessage}";
                return;
            }

            if (result.IsUpdateAvailable)
            {
                IsUpdateAvailable = true;
                LatestAppVersion = result.LatestVersionTag;
                LatestReleaseName = result.ReleaseName;
                ReleaseNotes = result.ReleaseNotes;
                ReleasePageUrl = result.ReleasePageUrl;
                _publishedAt = result.PublishedAt;
                OnPropertyChanged(nameof(PublishedDateText));
                UpdateStatusMessage = $"Update {result.LatestVersionTag} is available!";
            }
            else
            {
                IsUpdateAvailable = false;
                UpdateStatusMessage = isManual ? $"✓ GotifyWin {CurrentAppVersion} is up to date." : string.Empty;
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    public async Task DownloadAndInstallUpdateAsync()
    {
        if (_lastCheckResult?.ReleaseInfo == null || IsDownloadingUpdate) return;

        IsDownloadingUpdate = true;
        IsUpdateReadyToInstall = false;
        DownloadProgress = 0;
        UpdateStatusMessage = "Preparing download...";

        try
        {
            var release = _lastCheckResult.ReleaseInfo;
            var chosenAsset = release.Assets.FirstOrDefault(a => a.BrowserDownloadUrl == _lastCheckResult.DownloadUrl)
                              ?? release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (chosenAsset == null)
            {
                UpdateStatusMessage = "No compatible archive found in the release.";
                return;
            }

            UpdateStatusMessage = $"Downloading {chosenAsset.Name}...";
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                UpdateStatusMessage = $"Downloading {chosenAsset.Name} ({p:0}%)...";
            });

            _downloadedZipPath = await _updateService.DownloadUpdateAsync(release, chosenAsset, progress);

            // Fetch SHA-256 Checksum
            UpdateStatusMessage = "Verifying cryptographic SHA-256 checksum...";
            var expectedHash = await _updateService.FetchChecksumForAssetAsync(release, chosenAsset.Name);

            if (!string.IsNullOrEmpty(expectedHash))
            {
                var isVerified = await _updateService.VerifyChecksumAsync(_downloadedZipPath, expectedHash);
                if (!isVerified)
                {
                    UpdateStatusMessage = "⚠️ Security error: SHA-256 checksum mismatch! Download deleted.";
                    try { File.Delete(_downloadedZipPath); }
                    catch (Exception ex) when (ex is IOException) { Debug.WriteLine($"Failed to delete corrupted download: {ex.Message}"); }
                    _downloadedZipPath = null;
                    return;
                }
            }

            IsUpdateReadyToInstall = true;
            UpdateStatusMessage = "✓ Update verified and ready to install.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            UpdateStatusMessage = $"Download failed: {ex.Message}";
            IsUpdateReadyToInstall = false;
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    public async Task ApplyUpdateAndRestartAsync()
    {
        if (string.IsNullOrEmpty(_downloadedZipPath) || !File.Exists(_downloadedZipPath))
        {
            UpdateStatusMessage = "Update file not found. Please download again.";
            return;
        }

        UpdateStatusMessage = "Applying update and restarting GotifyWin...";
        try
        {
            await _updateService.ApplyUpdateAndRestartAsync(_downloadedZipPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            UpdateStatusMessage = $"Update failed: {ex.Message}. Try running as Administrator.";
        }
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Debug.WriteLine($"Failed to open URL {url}: {ex.Message}");
            }
        }
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
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
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

    private static void ScheduleClipboardClear(string copiedToken)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            try
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (System.Windows.Clipboard.ContainsText() &&
                        System.Windows.Clipboard.GetText() == copiedToken)
                    {
                        System.Windows.Clipboard.Clear();
                    }
                });
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            {
                Debug.WriteLine($"Failed to clear clipboard: {ex.Message}");
            }
        });
    }

    private string _testNotificationStatus = string.Empty;
    public string TestNotificationStatus
    {
        get => _testNotificationStatus;
        set => SetProperty(ref _testNotificationStatus, value);
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

        TestNotificationStatus = SelectedMode == NotificationMode.NativeToast
            ? "✓ Sent to Windows Notification Center"
            : "✓ Test banner dispatched";

        _ = Task.Delay(3500).ContinueWith(_ =>
        {
            UiDispatcher.RunOnUI(() => TestNotificationStatus = string.Empty);
        });
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
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
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
                ImportValidator.EnsureFileSizeAllowed(dlg.FileName);
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

                // SEC: Clamp all imported values into the ranges the UI itself allows.
                ImportValidator.ClampConfig(loaded);

                // SEC: Only accept a valid http(s) server URL. Never let an imported file redirect the
                // existing token to a different server: adopt the URL only if the file brings its own
                // token, or if no server is configured yet.
                var hasImportedToken = !string.IsNullOrWhiteSpace(loaded.ClientToken);
                if (ServerUrlValidator.Validate(loaded.ServerUrl).IsValid &&
                    (hasImportedToken || string.IsNullOrWhiteSpace(_configService.Config.ServerUrl)))
                {
                    _configService.Config.ServerUrl = loaded.ServerUrl!.Trim().TrimEnd('/');
                }
                if (hasImportedToken)
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
                    _configService.Config.Dnd.RetainGameDndOnAltTab = loaded.Dnd.RetainGameDndOnAltTab;
                }

                if (loaded.AppFilters != null)
                {
                    _configService.Config.AppFilters = ImportValidator.SanitizeAppFilters(loaded.AppFilters);
                }

                if (loaded.Rules != null && loaded.Rules.Count > 0)
                {
                    var safeRules = ImportValidator.SanitizeRules(loaded.Rules, out _);
                    if (safeRules.Count > 0)
                    {
                        _configService.Config.Rules = safeRules;
                    }
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
                OnPropertyChanged(nameof(RetainGameDndOnAltTab));
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
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            BackupStatusText = $"Import error: {ex.Message}";
        }
    }
}

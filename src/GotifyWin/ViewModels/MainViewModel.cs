using System.Net.Http;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Application = System.Windows.Application;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;

namespace GotifyWin.ViewModels;

public class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigService _configService;
    private readonly IGotifyApiService _apiService;
    private readonly IGotifyWebSocketService _wsService;
    private readonly IFilterEngine _filterEngine;
    private readonly IDndManager _dndManager;
    private readonly INotificationPresenter _presenter;
    private readonly INotificationPatternAnalyzer _patternAnalyzer;
    private readonly ISessionNotificationTracker _sessionTracker;
    public ISessionNotificationTracker SessionTracker => _sessionTracker;

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    private string _connectionStatusText = "Disconnected";
    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        set => SetProperty(ref _connectionStatusText, value);
    }

    private string _connectionStatusColor = "#EF4444";
    public string ConnectionStatusColor
    {
        get => _connectionStatusColor;
        set => SetProperty(ref _connectionStatusColor, value);
    }

    private string _dndStatusText = "DND: Off";
    public string DndStatusText
    {
        get => _dndStatusText;
        set => SetProperty(ref _dndStatusText, value);
    }

    private string _dndButtonLabel = "DND: Off";
    public string DndButtonLabel
    {
        get => _dndButtonLabel;
        set => SetProperty(ref _dndButtonLabel, value);
    }

    private bool _isDndActive;
    public bool IsDndActive
    {
        get => _isDndActive;
        set => SetProperty(ref _isDndActive, value);
    }

    public FeedViewModel Feed { get; }
    public AppsViewModel Apps { get; }
    public RulesViewModel Rules { get; }
    public SettingsViewModel Settings { get; }
    public string AppVersionDisplay => Settings.CurrentAppVersion;

    public ICommand ToggleDndCommand { get; }
    public ICommand DndIndefiniteCommand { get; }
    public ICommand Snooze30mCommand { get; }
    public ICommand Snooze1hCommand { get; }
    public ICommand Snooze4hCommand { get; }
    public ICommand SnoozeTomorrowCommand { get; }
    public ICommand DisableDndCommand { get; }
    public ICommand ReconnectCommand { get; }
    public ICommand NavigateToUpdatesCommand { get; }

    private bool _isReconnecting;
    public bool IsReconnecting
    {
        get => _isReconnecting;
        private set => SetProperty(ref _isReconnecting, value);
    }

    public MainViewModel(
        IConfigService configService,
        IGotifyApiService apiService,
        IGotifyWebSocketService wsService,
        IFilterEngine filterEngine,
        IDndManager dndManager,
        INotificationPresenter presenter,
        IStartupManager startupManager,
        INotificationPatternAnalyzer? patternAnalyzer = null,
        IThemeManager? themeManager = null,
        IUpdateService? updateService = null,
        ISessionNotificationTracker? sessionTracker = null,
        ISoundService? soundService = null)
    {
        _configService = configService;
        _apiService = apiService;
        _wsService = wsService;
        _filterEngine = filterEngine;
        _dndManager = dndManager;
        _presenter = presenter;
        _patternAnalyzer = patternAnalyzer ?? new NotificationPatternAnalyzer();
        _sessionTracker = sessionTracker ?? new SessionNotificationTracker();

        Feed = new FeedViewModel(_apiService, _configService, _presenter);
        Apps = new AppsViewModel(_apiService, _configService, soundService);
        Rules = new RulesViewModel(
            _configService,
            _patternAnalyzer,
            () => Feed.Messages,
            async () => await FetchRecentMessagesAsync()
        );
        Settings = new SettingsViewModel(_configService, _apiService, startupManager, _presenter, themeManager, updateService, soundService);

        ToggleDndCommand = new RelayCommand(ToggleDnd);
        DndIndefiniteCommand = new RelayCommand(SetDndIndefinite);
        Snooze30mCommand = new RelayCommand(() => SetDnd(TimeSpan.FromMinutes(30)));
        Snooze1hCommand = new RelayCommand(() => SetDnd(TimeSpan.FromHours(1)));
        Snooze4hCommand = new RelayCommand(() => SetDnd(TimeSpan.FromHours(4)));
        SnoozeTomorrowCommand = new RelayCommand(SetDndUntilTomorrow);
        DisableDndCommand = new RelayCommand(DisableDnd);
        ReconnectCommand = new RelayCommand(async () => await ReconnectAsync());
        NavigateToUpdatesCommand = new RelayCommand(NavigateToUpdates);

        // Wire events
        _wsService.MessageReceived += OnWebSocketMessageReceived;
        _wsService.ConnectionStateChanged += OnConnectionStateChanged;
        Settings.ConnectionConfigSaved += async (s, e) =>
        {
            await Apps.LoadAppsAsync();
            await LoadRecentHistoryAsync();
            await ReconnectAsync();
        };
        Apps.AppFilterChanged += () =>
        {
            Feed.RefreshFilter();
            Rules.PruneRecommendationsForDisabledApps();
        };
        Feed.UrlOpenRequested += (s, url) => _presenter.OpenUrl(url);
        if (_presenter is NotificationPresenter np)
        {
            np.MuteAppRequested += (s, appId) => Apps.MuteApp(appId);
        }
        Feed.CreateRuleRequested += (s, msg) =>
        {
            Rules.PrepareManualRuleFromNotification(msg);
            SelectedTabIndex = 2;
        };

        // DND periodic refresh timer (to update UI when quiet hours or snooze expires)
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        timer.Tick += (s, e) => UpdateDndStatus();
        timer.Start();

        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch (PlatformNotSupportedException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainVM] SessionSwitch hook note: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainVM] SessionSwitch hook note: {ex.Message}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainVM] SessionSwitch hook note: {ex.Message}");
        }

        UpdateDndStatus();
    }

    public async Task InitializeAsync()
    {
        _configService.Load();
        UpdateDndStatus();

        var cfg = _configService.Config;
        if (!string.IsNullOrEmpty(cfg.ServerUrl) && !string.IsNullOrEmpty(cfg.ClientToken))
        {
            _ = Task.Run(async () => await Apps.LoadAppsAsync());
            _ = Task.Run(async () => await LoadRecentHistoryAsync());
            await ReconnectAsync();
        }
    }

    public async Task LoadRecentHistoryAsync()
    {
        var cfg = _configService.Config;
        if (!string.IsNullOrEmpty(cfg.ServerUrl) && !string.IsNullOrEmpty(cfg.ClientToken))
        {
            try
            {
                var recent = await _apiService.GetRecentMessagesAsync(cfg.ServerUrl, cfg.ClientToken, 50);
                EnrichMessages(recent);

                var isDnd = _dndManager.IsDndActive(cfg.Dnd);
                var allowed = recent.Where(m =>
                {
                    var d = _filterEngine.Evaluate(m, cfg, isDnd);
                    return d.Decision != FilterDecision.Drop;
                }).ToList();

                Feed.SetInitialMessages(allowed);

                var recs = _patternAnalyzer.Analyze(recent, _configService.Config, threshold: 3);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Rules.SetRecommendations(recs);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainVM] Failed to load recent history: {ex.Message}");
            }
        }
    }

    private async Task<IEnumerable<GotifyMessage>> FetchRecentMessagesAsync()
    {
        var cfg = _configService.Config;
        if (!string.IsNullOrEmpty(cfg.ServerUrl) && !string.IsNullOrEmpty(cfg.ClientToken))
        {
            try
            {
                var recent = await _apiService.GetRecentMessagesAsync(cfg.ServerUrl, cfg.ClientToken, 50);
                EnrichMessages(recent);
                return recent;
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainVM] FetchRecentMessages note: {ex.Message}");
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainVM] FetchRecentMessages note: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainVM] FetchRecentMessages note: {ex.Message}");
            }
        }
        return Enumerable.Empty<GotifyMessage>();
    }

    private void EnrichMessages(IEnumerable<GotifyMessage> messages)
    {
        foreach (var msg in messages)
        {
            EnrichMessage(msg);
        }
    }

    private void EnrichMessage(GotifyMessage msg)
    {
        if (_configService.Config.AppFilters.TryGetValue(msg.AppId, out var setting))
        {
            msg.ApplicationName = setting.AppName;
            var localIcon = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GotifyWin", "cache", "icons", $"app_{msg.AppId}.png"
            );
            if (File.Exists(localIcon))
            {
                msg.ApplicationIconPath = localIcon;
            }
        }
    }

    private async void OnWebSocketMessageReceived(object? sender, GotifyMessage message)
    {
        try
        {
            await HandleIncomingMessageAsync(message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[GotifyWin] Message processing error: {ex.Message}");
        }
    }

    internal async Task HandleIncomingMessageAsync(GotifyMessage message)
    {
        // Issue #44: If a message arrives for an unknown AppId, sync apps immediately before evaluating/displaying
        if (!_configService.Config.AppFilters.ContainsKey(message.AppId) &&
            (DateTime.UtcNow - Apps.LastSyncUtc).TotalSeconds >= 5)
        {
            var syncTask = Apps.LoadAppsAsync();
            await Task.WhenAny(syncTask, Task.Delay(2500));

            if (syncTask.IsCompletedSuccessfully)
            {
                foreach (var discoveryNotice in syncTask.Result
                    .Where(a => !a.Enabled)
                    .Select(newApp => new GotifyMessage
                    {
                        Id = -newApp.AppId,
                        AppId = 0,
                        ApplicationName = "GotifyWin",
                        Title = $"New Gotify App: {newApp.AppName}",
                        Message = $"'{newApp.AppName}' was discovered and muted by default. Open the Apps tab to enable notifications.",
                        Priority = 5,
                        Date = DateTime.UtcNow
                    }))
                {
                    _presenter.Present(discoveryNotice, _configService.Config);
                }
            }
        }

        EnrichMessage(message);

        var isDnd = _dndManager.IsDndActive(_configService.Config.Dnd);
        var decision = _filterEngine.Evaluate(message, _configService.Config, isDnd);

        System.Diagnostics.Debug.WriteLine($"[GotifyWin] Message #{message.Id} from '{message.DisplayAppName}' (Priority: {message.Priority}) -> {decision.Decision}: {decision.Reason}");

        switch (decision.Decision)
        {
            case FilterDecision.Drop:
                return;

            case FilterDecision.SilentLog:
                Feed.AddMessage(message);
                break;

            case FilterDecision.Notify:
                _sessionTracker.RecordNotification(message, _configService.Config.Presentation.MissedSummaryMinPriority);
                Feed.AddMessage(message);
                _presenter.Present(message, _configService.Config);
                break;
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        System.Diagnostics.Debug.WriteLine($"[GotifyWin] WebSocket state: {state}");
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            switch (state)
            {
                case ConnectionState.Connected:
                    ConnectionStatusText = "Connected";
                    ConnectionStatusColor = "#10B981"; // Green
                    IsReconnecting = false;
                    if ((DateTime.UtcNow - Apps.LastSyncUtc).TotalSeconds >= 15)
                    {
                        _ = Task.Run(() => Apps.LoadAppsAsync());
                    }
                    break;
                case ConnectionState.Connecting:
                    ConnectionStatusText = "Connecting...";
                    ConnectionStatusColor = "#F59E0B"; // Amber
                    IsReconnecting = true;
                    break;
                case ConnectionState.Reconnecting:
                    ConnectionStatusText = "Reconnecting...";
                    ConnectionStatusColor = "#F59E0B"; // Amber
                    IsReconnecting = true;
                    break;
                case ConnectionState.Disconnected:
                    ConnectionStatusText = "Disconnected";
                    ConnectionStatusColor = "#EF4444"; // Red
                    IsReconnecting = false;
                    break;
            }
        });
    }

    public async Task ReconnectAsync()
    {
        var cfg = _configService.Config;
        if (!string.IsNullOrEmpty(cfg.ServerUrl) && !string.IsNullOrEmpty(cfg.ClientToken))
        {
            IsReconnecting = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await _wsService.ConnectAsync(cfg.ServerUrl, cfg.ClientToken);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainVM] Reconnect failed: {ex.Message}");
            }
            finally
            {
                // Ensure at least 600ms of visual feedback so user perceives the action
                if (sw.ElapsedMilliseconds < 600)
                {
                    await Task.Delay(600 - (int)sw.ElapsedMilliseconds);
                }
                if (_wsService.State != ConnectionState.Connecting && _wsService.State != ConnectionState.Reconnecting)
                {
                    IsReconnecting = false;
                }
            }
        }
    }

    public void ToggleDnd()
    {
        if (IsDndActive)
        {
            DisableDnd();
        }
        else
        {
            SetDnd(TimeSpan.FromHours(1)); // Default toggle = 1h snooze
        }
    }

    public void SetDndIndefinite()
    {
        _dndManager.SetManualDnd(_configService.Config.Dnd, null);
        _configService.Save();
        UpdateDndStatus();
    }

    public void SetDnd(TimeSpan duration)
    {
        _dndManager.SetManualDnd(_configService.Config.Dnd, duration);
        _configService.Save();
        UpdateDndStatus();
    }

    public void SetDndUntilTomorrow()
    {
        var now = DateTime.Now;
        var tomorrow8am = now.Date.AddDays(1).AddHours(8);
        var duration = tomorrow8am - now;
        _dndManager.SetManualDnd(_configService.Config.Dnd, duration);
        _configService.Save();
        UpdateDndStatus();
    }

    public void DisableDnd()
    {
        _dndManager.DisableManualDnd(_configService.Config.Dnd);
        _configService.Save();
        UpdateDndStatus();
    }

    private void UpdateDndStatus()
    {
        var dnd = _configService.Config.Dnd;
        IsDndActive = _dndManager.IsDndActive(dnd);
        DndStatusText = _dndManager.GetStatusDescription(dnd);

        if (!IsDndActive)
        {
            DndButtonLabel = "DND: Off";
        }
        else if (dnd.IsManualDndActive)
        {
            if (dnd.ManualDndUntilUtc.HasValue)
            {
                var remaining = dnd.ManualDndUntilUtc.Value - DateTime.UtcNow;
                if (remaining.TotalMinutes > 0 && remaining.TotalMinutes <= 60)
                {
                    DndButtonLabel = $"DND: {(int)Math.Ceiling(remaining.TotalMinutes)}m left";
                }
                else if (remaining.TotalMinutes > 60)
                {
                    DndButtonLabel = $"DND: {dnd.ManualDndUntilUtc.Value.ToLocalTime():HH:mm}";
                }
                else
                {
                    DndButtonLabel = "DND: Active";
                }
            }
            else
            {
                DndButtonLabel = "DND: Active";
            }
        }
        else if (dnd.QuietHoursEnabled && _dndManager.IsQuietHours(dnd))
        {
            DndButtonLabel = "DND: Quiet";
        }
        else if (dnd.SuppressOnFullscreenApps && _dndManager.IsFullscreenAppRunning())
        {
            var fs = _dndManager.GetFullscreenState();
            DndButtonLabel = fs switch
            {
                FullscreenState.Game => "DND: Game",
                FullscreenState.Presentation => "DND: Present",
                FullscreenState.Busy => "DND: Media",
                _ => "DND: Active"
            };
        }
        else
        {
            DndButtonLabel = "DND: Active";
        }
    }

    private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
        {
            _sessionTracker.OnSessionLocked();
        }
        else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
        {
            var summary = _sessionTracker.OnSessionUnlocked(_configService.Config.Presentation);
            if (summary != null)
            {
                UiDispatcher.RunOnUI(() =>
                {
                    Feed.AddMessage(summary);
                    _presenter.Present(summary, _configService.Config);
                });
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
        catch (PlatformNotSupportedException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainVM] SessionSwitch unhook note: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainVM] SessionSwitch unhook note: {ex.Message}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainVM] SessionSwitch unhook note: {ex.Message}");
        }

        Feed.Dispose();
        GC.SuppressFinalize(this);
    }

    private void NavigateToUpdates()
    {
        SelectedTabIndex = 3;
        Settings.ScrollToAbout();
    }
}

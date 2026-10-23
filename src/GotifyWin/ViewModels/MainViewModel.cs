using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Application = System.Windows.Application;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;

namespace GotifyWin.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IGotifyApiService _apiService;
    private readonly IGotifyWebSocketService _wsService;
    private readonly IFilterEngine _filterEngine;
    private readonly IDndManager _dndManager;
    private readonly INotificationPresenter _presenter;
    private readonly INotificationPatternAnalyzer _patternAnalyzer;

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

    public ICommand ToggleDndCommand { get; }
    public ICommand Snooze30mCommand { get; }
    public ICommand Snooze1hCommand { get; }
    public ICommand Snooze4hCommand { get; }
    public ICommand SnoozeTomorrowCommand { get; }
    public ICommand DisableDndCommand { get; }
    public ICommand ReconnectCommand { get; }

    public MainViewModel(
        IConfigService configService,
        IGotifyApiService apiService,
        IGotifyWebSocketService wsService,
        IFilterEngine filterEngine,
        IDndManager dndManager,
        INotificationPresenter presenter,
        IStartupManager startupManager,
        INotificationPatternAnalyzer? patternAnalyzer = null,
        IThemeManager? themeManager = null)
    {
        _configService = configService;
        _apiService = apiService;
        _wsService = wsService;
        _filterEngine = filterEngine;
        _dndManager = dndManager;
        _presenter = presenter;
        _patternAnalyzer = patternAnalyzer ?? new NotificationPatternAnalyzer();

        Feed = new FeedViewModel(_apiService, _configService, _presenter);
        Apps = new AppsViewModel(_apiService, _configService);
        Rules = new RulesViewModel(
            _configService,
            _patternAnalyzer,
            () => Feed.Messages,
            async () => await FetchRecentMessagesAsync()
        );
        Settings = new SettingsViewModel(_configService, _apiService, startupManager, _presenter, themeManager);

        ToggleDndCommand = new RelayCommand(ToggleDnd);
        Snooze30mCommand = new RelayCommand(() => SetDnd(TimeSpan.FromMinutes(30)));
        Snooze1hCommand = new RelayCommand(() => SetDnd(TimeSpan.FromHours(1)));
        Snooze4hCommand = new RelayCommand(() => SetDnd(TimeSpan.FromHours(4)));
        SnoozeTomorrowCommand = new RelayCommand(SetDndUntilTomorrow);
        DisableDndCommand = new RelayCommand(DisableDnd);
        ReconnectCommand = new RelayCommand(async () => await ReconnectAsync());

        // Wire events
        _wsService.MessageReceived += OnWebSocketMessageReceived;
        _wsService.ConnectionStateChanged += OnConnectionStateChanged;
        Settings.ConnectionConfigSaved += async (s, e) =>
        {
            await Apps.LoadAppsAsync();
            await LoadRecentHistoryAsync();
            await ReconnectAsync();
        };
        Apps.AppFilterChanged += () => Feed.RefreshFilter();
        Feed.UrlOpenRequested += (s, url) => _presenter.OpenUrl(url);
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
            catch { }
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

    private void OnWebSocketMessageReceived(object? sender, GotifyMessage message)
    {
        EnrichMessage(message);

        var isDnd = _dndManager.IsDndActive(_configService.Config.Dnd);
        var decision = _filterEngine.Evaluate(message, _configService.Config, isDnd);

        Console.WriteLine($"[GotifyWin] Message #{message.Id} from '{message.DisplayAppName}' (Priority: {message.Priority}) -> {decision.Decision}: {decision.Reason}");

        switch (decision.Decision)
        {
            case FilterDecision.Drop:
                return;

            case FilterDecision.SilentLog:
                Feed.AddMessage(message);
                break;

            case FilterDecision.Notify:
                Feed.AddMessage(message);
                _presenter.Present(message, _configService.Config);
                break;
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        Console.WriteLine($"[GotifyWin] WebSocket state: {state}");
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            switch (state)
            {
                case ConnectionState.Connected:
                    ConnectionStatusText = "Connected";
                    ConnectionStatusColor = "#10B981"; // Green
                    break;
                case ConnectionState.Connecting:
                    ConnectionStatusText = "Connecting...";
                    ConnectionStatusColor = "#F59E0B"; // Amber
                    break;
                case ConnectionState.Reconnecting:
                    ConnectionStatusText = "Reconnecting...";
                    ConnectionStatusColor = "#F59E0B"; // Amber
                    break;
                case ConnectionState.Disconnected:
                    ConnectionStatusText = "Disconnected";
                    ConnectionStatusColor = "#EF4444"; // Red
                    break;
            }
        });
    }

    public async Task ReconnectAsync()
    {
        var cfg = _configService.Config;
        if (!string.IsNullOrEmpty(cfg.ServerUrl) && !string.IsNullOrEmpty(cfg.ClientToken))
        {
            try
            {
                await _wsService.ConnectAsync(cfg.ServerUrl, cfg.ClientToken);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainVM] Reconnect failed: {ex.Message}");
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
            DndButtonLabel = "DND: Gaming";
        }
        else
        {
            DndButtonLabel = "DND: Active";
        }
    }
}

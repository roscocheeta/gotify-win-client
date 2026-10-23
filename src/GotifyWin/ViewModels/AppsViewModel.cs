using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Application = System.Windows.Application;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;

namespace GotifyWin.ViewModels;

public class AppItemViewModel : ViewModelBase
{
    private readonly AppFilterSetting _setting;
    private readonly Action _onChanged;

    public long Id => _setting.AppId;
    public string Name => _setting.AppName;

    public string? Description { get; set; }
    public string? IconPath { get; set; }

    public bool HasIcon => !string.IsNullOrEmpty(IconPath) && File.Exists(IconPath);

    public void UpdateIcon(string path)
    {
        IconPath = path;
        OnPropertyChanged(nameof(IconPath));
        OnPropertyChanged(nameof(HasIcon));
    }

    public bool IsEnabled
    {
        get => _setting.Enabled;
        set
        {
            if (_setting.Enabled != value)
            {
                _setting.Enabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(StatusColor));
                _onChanged();
            }
        }
    }

    public string StatusLabel => IsEnabled ? "Notifications On" : "Notifications Muted";
    public string StatusColor => IsEnabled ? "#10B981" : "#94A3B8";

    public int MinPriority
    {
        get => _setting.MinPriority;
        set
        {
            if (_setting.MinPriority != value)
            {
                _setting.MinPriority = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MinPriorityText));
                OnPropertyChanged(nameof(MinPriorityDescription));
                _onChanged();
            }
        }
    }

    public string MinPriorityText => MinPriority switch
    {
        0 => "All Messages (0+)",
        <= 3 => $"\u2265 {MinPriority} (Low+)",
        <= 6 => $"\u2265 {MinPriority} (Normal+)",
        7 => $"\u2265 7 (High+)",
        _ => $"\u2265 {MinPriority} (Urgent)"
    };

    public string MinPriorityDescription => MinPriority == 0
        ? "All messages from this application will show desktop notifications."
        : $"Messages below priority {MinPriority} will be logged silently without desktop notifications.";

    public bool OverrideGlobalDnd
    {
        get => _setting.OverrideGlobalDnd;
        set
        {
            if (_setting.OverrideGlobalDnd != value)
            {
                _setting.OverrideGlobalDnd = value;
                OnPropertyChanged();
                _onChanged();
            }
        }
    }

    public AppItemViewModel(AppFilterSetting setting, Action onChanged)
    {
        _setting = setting;
        _onChanged = onChanged;
    }
}

public class AppsViewModel : ViewModelBase
{
    private readonly IGotifyApiService _apiService;
    private readonly IConfigService _configService;
    private readonly ObservableCollection<AppItemViewModel> _apps = new();

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ObservableCollection<AppItemViewModel> Apps => _apps;

    public event Action? AppFilterChanged;

    private string _syncButtonText = "🔄 Sync Apps";
    public string SyncButtonText
    {
        get => _syncButtonText;
        set => SetProperty(ref _syncButtonText, value);
    }

    private bool _isSyncing;
    public bool IsSyncing
    {
        get => _isSyncing;
        set => SetProperty(ref _isSyncing, value);
    }

    public ICommand RefreshAppsCommand { get; }
    public ICommand EnableAllCommand { get; }
    public ICommand DisableAllCommand { get; }

    public AppsViewModel(IGotifyApiService apiService, IConfigService configService)
    {
        _apiService = apiService;
        _configService = configService;

        RefreshAppsCommand = new RelayCommand(async () =>
        {
            if (_isSyncing) return;
            _isSyncing = true;
            SyncButtonText = "⏳ Syncing...";
            try
            {
                await LoadAppsAsync();
                SyncButtonText = "✓ Synced";
                await Task.Delay(1400);
            }
            finally
            {
                SyncButtonText = "🔄 Sync Apps";
                _isSyncing = false;
            }
        });
        EnableAllCommand = new RelayCommand(() => SetAllEnabled(true));
        DisableAllCommand = new RelayCommand(() => SetAllEnabled(false));
    }

    private void OnFilterChanged()
    {
        _configService.Save();
        AppFilterChanged?.Invoke();
    }

    public async Task LoadAppsAsync()
    {
        var cfg = _configService.Config;
        if (string.IsNullOrEmpty(cfg.ServerUrl) || string.IsNullOrEmpty(cfg.ClientToken))
        {
            StatusMessage = "Configure Server URL & Token in Settings first.";
            return;
        }

        IsLoading = true;
        StatusMessage = "Discovering apps...";

        try
        {
            var apiApps = await _apiService.GetApplicationsAsync(cfg.ServerUrl, cfg.ClientToken);

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _apps.Clear();

                foreach (var app in apiApps)
                {
                    if (!cfg.AppFilters.TryGetValue(app.Id, out var filter))
                    {
                        filter = new AppFilterSetting
                        {
                            AppId = app.Id,
                            AppName = app.Name,
                            Enabled = true,
                            MinPriority = 0
                        };
                        cfg.AppFilters[app.Id] = filter;
                    }
                    else
                    {
                        filter.AppName = app.Name; // keep sync
                    }

                    var itemVm = new AppItemViewModel(filter, OnFilterChanged)
                    {
                        Description = app.Description,
                        IconPath = app.LocalIconPath
                    };

                    _apps.Add(itemVm);
                }

                _configService.Save();
                AppFilterChanged?.Invoke();
                StatusMessage = $"Discovered {_apps.Count} apps from Gotify.";
            });

            // Cache icons in background
            foreach (var app in apiApps)
            {
                if (string.IsNullOrEmpty(app.LocalIconPath))
                {
                    var icon = await _apiService.CacheApplicationIconAsync(cfg.ServerUrl, cfg.ClientToken, app);
                    if (!string.IsNullOrEmpty(icon))
                    {
                        var matchingVm = _apps.FirstOrDefault(a => a.Id == app.Id);
                        if (matchingVm != null)
                        {
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                matchingVm.UpdateIcon(icon);
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetAllEnabled(bool enabled)
    {
        foreach (var app in _apps)
        {
            app.IsEnabled = enabled;
        }
        _configService.Save();
        AppFilterChanged?.Invoke();
    }
}

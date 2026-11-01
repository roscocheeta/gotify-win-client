using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using GotifyWin.Models;
using GotifyWin.Services;

namespace GotifyWin.ViewModels;

public class FeedViewModel : ViewModelBase, IDisposable
{
    private readonly IGotifyApiService _apiService;
    private readonly IConfigService _configService;
    private readonly INotificationPresenter? _presenter;
    private readonly ObservableCollection<GotifyMessage> _messages = new();
    public ObservableCollection<GotifyMessage> Messages => _messages;
    private readonly ICollectionView _filteredView;
    private readonly System.Windows.Threading.DispatcherTimer? _relativeTimeTimer;

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _filteredView.Refresh();
                OnPropertyChanged(nameof(HasMessages));
            }
        }
    }

    private string _selectedAppFilter = "All Apps";
    public string SelectedAppFilter
    {
        get => _selectedAppFilter;
        set
        {
            if (SetProperty(ref _selectedAppFilter, value))
            {
                _filteredView.Refresh();
                OnPropertyChanged(nameof(HasMessages));
            }
        }
    }

    private ObservableCollection<string> _availableAppFilters = new() { "All Apps" };
    public ObservableCollection<string> AvailableAppFilters
    {
        get => _availableAppFilters;
        set => SetProperty(ref _availableAppFilters, value);
    }

    private bool _isCompactView;
    public bool IsCompactView
    {
        get => _isCompactView;
        set
        {
            if (SetProperty(ref _isCompactView, value))
            {
                _configService.Config.UseCompactFeedView = value;
                _configService.Save();
            }
        }
    }

    private bool _hideFilteredMessages;
    public bool HideFilteredMessages
    {
        get => _hideFilteredMessages;
        set
        {
            if (SetProperty(ref _hideFilteredMessages, value))
            {
                _configService.Config.HideFilteredInFeed = value;
                _configService.Save();
                _filteredView.Refresh();
                OnPropertyChanged(nameof(HasMessages));
                OnPropertyChanged(nameof(FilteredOutCount));
            }
        }
    }

    public int FilteredOutCount => _messages.Count(m => m.IsFiltered);

    public ICollectionView FilteredMessages => _filteredView;

    public bool HasMessages => !_filteredView.IsEmpty;

    public ICommand ClearAllCommand { get; }
    public ICommand DeleteMessageCommand { get; }
    public ICommand CopyMessageCommand { get; }
    public ICommand CreateRuleCommand { get; }
    public ICommand OpenUrlCommand { get; }
    public ICommand TestNotificationCommand { get; }

    public event EventHandler<string>? UrlOpenRequested;
    public event EventHandler<GotifyMessage>? CreateRuleRequested;

    public FeedViewModel(IGotifyApiService apiService, IConfigService configService, INotificationPresenter? presenter = null)
    {
        _apiService = apiService;
        _configService = configService;
        _presenter = presenter;

        _filteredView = CollectionViewSource.GetDefaultView(_messages);
        _filteredView.Filter = FilterMessagePredicate;
        _filteredView.SortDescriptions.Clear();
        _filteredView.SortDescriptions.Add(new SortDescription(nameof(GotifyMessage.Date), ListSortDirection.Descending));
        _filteredView.SortDescriptions.Add(new SortDescription(nameof(GotifyMessage.Id), ListSortDirection.Descending));

        try
        {
            _relativeTimeTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _relativeTimeTimer.Tick += (s, e) => RefreshRelativeTimes();
            _relativeTimeTimer.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            System.Diagnostics.Debug.WriteLine($"[FeedVM] DispatcherTimer init note: {ex.Message}");
        }


        _isCompactView = _configService.Config.UseCompactFeedView;
        _hideFilteredMessages = _configService.Config.HideFilteredInFeed;

        ClearAllCommand = new RelayCommand(ClearAll);
        DeleteMessageCommand = new RelayCommand<GotifyMessage>(DeleteMessage);
        CopyMessageCommand = new RelayCommand<GotifyMessage>(CopyMessage);
        CreateRuleCommand = new RelayCommand<GotifyMessage>(CreateRule);
        OpenUrlCommand = new RelayCommand<string>(OpenUrl);
        TestNotificationCommand = new RelayCommand(SendTestNotification);
    }

    private void CreateRule(GotifyMessage? message)
    {
        if (message != null)
        {
            CreateRuleRequested?.Invoke(this, message);
        }
    }

    private void SendTestNotification()
    {
        var testMsg = new GotifyMessage
        {
            Id = DateTime.UtcNow.Ticks,
            AppId = 2,
            Title = "Test Gotify Notification",
            Message = "This is a live test notification from GotifyWin verifying desktop popup and sound.",
            Priority = 6,
            Date = DateTime.UtcNow,
            ApplicationName = "GotifyWin",
            Extras = new Dictionary<string, System.Text.Json.JsonElement>()
        };

        AddMessage(testMsg);
        _presenter?.Present(testMsg, _configService.Config);
    }

    public void AddMessage(GotifyMessage message)
    {
        UiDispatcher.RunOnUI(() =>
        {
            // Avoid duplicate entries if message already loaded
            if (message.Id > 0 && _messages.Any(m => m.Id == message.Id))
            {
                return;
            }

            // Strictly preserve chronological order (newest Date and Id first)
            int insertIndex = 0;
            while (insertIndex < _messages.Count &&
                   (_messages[insertIndex].Date > message.Date ||
                   (_messages[insertIndex].Date == message.Date && _messages[insertIndex].Id > message.Id)))
            {
                insertIndex++;
            }
            _messages.Insert(insertIndex, message);

            // Cap message history
            var max = _configService.Config.MaxHistoryCount;
            while (_messages.Count > max && _messages.Count > 0)
            {
                _messages.RemoveAt(_messages.Count - 1);
            }

            // Update app filter options if new app appears
            if (!string.IsNullOrEmpty(message.ApplicationName) && !_availableAppFilters.Contains(message.ApplicationName))
            {
                _availableAppFilters.Add(message.ApplicationName);
            }

            OnPropertyChanged(nameof(HasMessages));
            OnPropertyChanged(nameof(FilteredOutCount));
        });
    }

    public void AddMessages(IEnumerable<GotifyMessage> messages)
    {
        UiDispatcher.RunOnUI(() =>
        {
            var msgList = messages.OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).ToList();
            if (msgList.Count == 0) return;

            var max = _configService.Config.MaxHistoryCount;
            foreach (var m in msgList)
            {
                if (m.Id > 0 && _messages.Any(existing => existing.Id == m.Id))
                {
                    continue;
                }

                int insertIndex = 0;
                while (insertIndex < _messages.Count &&
                       (_messages[insertIndex].Date > m.Date ||
                       (_messages[insertIndex].Date == m.Date && _messages[insertIndex].Id > m.Id)))
                {
                    insertIndex++;
                }
                _messages.Insert(insertIndex, m);
                if (!string.IsNullOrEmpty(m.ApplicationName) && !_availableAppFilters.Contains(m.ApplicationName))
                {
                    _availableAppFilters.Add(m.ApplicationName);
                }
            }

            while (_messages.Count > max && _messages.Count > 0)
            {
                _messages.RemoveAt(_messages.Count - 1);
            }

            OnPropertyChanged(nameof(HasMessages));
            OnPropertyChanged(nameof(FilteredOutCount));
        });
    }

    public void SetInitialMessages(IEnumerable<GotifyMessage> messages)
    {
        UiDispatcher.RunOnUI(() =>
        {
            _messages.Clear();
            foreach (var m in messages.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id))
            {
                _messages.Add(m);
                if (!string.IsNullOrEmpty(m.ApplicationName) && !_availableAppFilters.Contains(m.ApplicationName))
                {
                    _availableAppFilters.Add(m.ApplicationName);
                }
            }
            _filteredView.Refresh();
            OnPropertyChanged(nameof(HasMessages));
            OnPropertyChanged(nameof(FilteredOutCount));
        });
    }

    public void RefreshFilter()
    {
        UiDispatcher.RunOnUI(() =>
        {
            _filteredView.Refresh();
            OnPropertyChanged(nameof(HasMessages));
            OnPropertyChanged(nameof(FilteredOutCount));
        });
    }

    public void RefreshFromConfig()
    {
        IsCompactView = _configService.Config.UseCompactFeedView;
        HideFilteredMessages = _configService.Config.HideFilteredInFeed;
        RefreshFilter();
    }

    private bool FilterMessagePredicate(object obj)
    {
        if (obj is not GotifyMessage msg) return false;

        // Hide messages filtered out by rules if user enabled Hide Filtered
        if (_hideFilteredMessages && msg.IsFiltered)
        {
            return false;
        }

        // App-level enablement: Hide notifications from apps that the user has disabled
        if (_configService.Config.AppFilters.TryGetValue(msg.AppId, out var appSetting) && !appSetting.Enabled)
        {
            return false;
        }

        // App filter dropdown
        if (_selectedAppFilter != "All Apps" && !string.Equals(msg.ApplicationName, _selectedAppFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Text search
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var query = _searchText.Trim();
            var inTitle = msg.Title?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            var inBody = msg.Message?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            var inApp = msg.ApplicationName?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            return inTitle || inBody || inApp;
        }

        return true;
    }

    private void ClearAll()
    {
        _messages.Clear();
        _filteredView.Refresh();
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(FilteredOutCount));

        // Optionally clear on server in background
        _ = Task.Run(async () =>
        {
            try
            {
                var cfg = _configService.Config;
                if (!string.IsNullOrEmpty(cfg.ServerUrl) && !string.IsNullOrEmpty(cfg.ClientToken))
                {
                    await _apiService.ClearAllMessagesAsync(cfg.ServerUrl, cfg.ClientToken);
                }
            }
            catch { }
        });
    }

    private void DeleteMessage(GotifyMessage? msg)
    {
        if (msg == null) return;
        _messages.Remove(msg);
        _filteredView.Refresh();
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(FilteredOutCount));

        _ = Task.Run(async () =>
        {
            try
            {
                var cfg = _configService.Config;
                if (!string.IsNullOrEmpty(cfg.ServerUrl) && !string.IsNullOrEmpty(cfg.ClientToken))
                {
                    await _apiService.DeleteMessageAsync(cfg.ServerUrl, cfg.ClientToken, msg.Id);
                }
            }
            catch { }
        });
    }

    private void CopyMessage(GotifyMessage? msg)
    {
        if (msg == null) return;
        try
        {
            var text = $"{msg.DisplayTitle}\n{msg.Message}";
            Clipboard.SetText(text);
        }
        catch { }
    }

    public void RefreshRelativeTimes()
    {
        UiDispatcher.RunOnUI(() =>
        {
            foreach (var m in _messages)
            {
                m.RefreshRelativeTime();
            }
        });
    }

    public void Dispose()
    {
        _relativeTimeTimer?.Stop();
        GC.SuppressFinalize(this);
    }

    private void OpenUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            UrlOpenRequested?.Invoke(this, url);
        }
    }
}

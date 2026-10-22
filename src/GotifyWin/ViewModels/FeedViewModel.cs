using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using GotifyWin.Models;
using GotifyWin.Services;

namespace GotifyWin.ViewModels;

public class FeedViewModel : ViewModelBase
{
    private readonly IGotifyApiService _apiService;
    private readonly IConfigService _configService;
    private readonly INotificationPresenter? _presenter;
    private readonly ObservableCollection<GotifyMessage> _messages = new();
    private readonly ICollectionView _filteredView;

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

    public ICollectionView FilteredMessages => _filteredView;

    public bool HasMessages => !_filteredView.IsEmpty;

    public ICommand ClearAllCommand { get; }
    public ICommand DeleteMessageCommand { get; }
    public ICommand CopyMessageCommand { get; }
    public ICommand OpenUrlCommand { get; }
    public ICommand TestNotificationCommand { get; }

    public event EventHandler<string>? UrlOpenRequested;

    public FeedViewModel(IGotifyApiService apiService, IConfigService configService, INotificationPresenter? presenter = null)
    {
        _apiService = apiService;
        _configService = configService;
        _presenter = presenter;

        _filteredView = CollectionViewSource.GetDefaultView(_messages);
        _filteredView.Filter = FilterMessagePredicate;

        ClearAllCommand = new RelayCommand(ClearAll);
        DeleteMessageCommand = new RelayCommand<GotifyMessage>(DeleteMessage);
        CopyMessageCommand = new RelayCommand<GotifyMessage>(CopyMessage);
        OpenUrlCommand = new RelayCommand<string>(OpenUrl);
        TestNotificationCommand = new RelayCommand(SendTestNotification);
    }

    private void SendTestNotification()
    {
        var testMsg = new GotifyMessage
        {
            Id = DateTime.UtcNow.Ticks,
            AppId = 2,
            Title = "Test Gotify Notification",
            Message = "This is a live test notification from GotifyWin verifying desktop popup and audio alerts.",
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
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            _messages.Insert(0, message);

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

            _filteredView.Refresh();
            OnPropertyChanged(nameof(HasMessages));
        });
    }

    public void SetInitialMessages(IEnumerable<GotifyMessage> messages)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            _messages.Clear();
            foreach (var m in messages.OrderByDescending(x => x.Date))
            {
                _messages.Add(m);
                if (!string.IsNullOrEmpty(m.ApplicationName) && !_availableAppFilters.Contains(m.ApplicationName))
                {
                    _availableAppFilters.Add(m.ApplicationName);
                }
            }
            _filteredView.Refresh();
            OnPropertyChanged(nameof(HasMessages));
        });
    }

    private bool FilterMessagePredicate(object obj)
    {
        if (obj is not GotifyMessage msg) return false;

        // App filter
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

    private void OpenUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            UrlOpenRequested?.Invoke(this, url);
        }
    }
}

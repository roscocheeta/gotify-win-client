using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using GotifyWin.Services;
using GotifyWin.ViewModels;

namespace GotifyWin;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IConfigService _configService;
    private NotifyIcon? _notifyIcon;
    private bool _isExplicitQuit;

    public MainWindow(MainViewModel viewModel, IConfigService configService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _configService = configService;
        DataContext = _viewModel;

        InitializeSystemTray();

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex))
            {
                SwitchTab(_viewModel.SelectedTabIndex);
            }
        };

        Loaded += async (s, e) =>
        {
            await _viewModel.InitializeAsync();
            SwitchTab(0);
        };
    }

    private void InitializeSystemTray()
    {
        System.Drawing.Icon? trayIcon = null;
        try
        {
            var streamInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/GotifyWin;component/Assets/app.ico")) ?? System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (streamInfo != null)
            {
                using var stream = streamInfo.Stream;
                // Windows 11 system tray renders at 24x24 or 32x32 (and up to 48x48 on high-DPI displays).
                // Using legacy SmallIconSize (16x16) forces Windows to render a tiny 16px icon centered in a 32px slot.
                // Using IconSize (minimum 32x32, scaling to 48x48 on high DPI) extracts the crisp, high-res frame
                // and lets the Windows Shell display a prominent, fully visible tray icon matching other modern apps.
                var iconSize = System.Windows.Forms.SystemInformation.IconSize;
                var targetWidth = Math.Max(32, iconSize.Width);
                var targetHeight = Math.Max(32, iconSize.Height);
                trayIcon = new System.Drawing.Icon(stream, targetWidth, targetHeight);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Could not load tray icon resource: {ex.Message}");
        }

        _notifyIcon = new NotifyIcon
        {
            Icon = trayIcon ?? SystemIcons.Information,
            Text = "Gotify Client",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open Gotify Client", null, (s, e) => ShowAndRestore());
        contextMenu.Items.Add(new ToolStripSeparator());

        var dndMenu = new ToolStripMenuItem("Do Not Disturb");
        dndMenu.DropDownItems.Add("Off (Show Notifications)", null, (s, e) => _viewModel.DisableDnd());
        dndMenu.DropDownItems.Add("On until I turn it off", null, (s, e) => _viewModel.SetDndIndefinite());
        dndMenu.DropDownItems.Add("Snooze 30 minutes", null, (s, e) => _viewModel.SetDnd(TimeSpan.FromMinutes(30)));
        dndMenu.DropDownItems.Add("Snooze 1 hour", null, (s, e) => _viewModel.SetDnd(TimeSpan.FromHours(1)));
        dndMenu.DropDownItems.Add("Snooze 4 hours", null, (s, e) => _viewModel.SetDnd(TimeSpan.FromHours(4)));
        dndMenu.DropDownItems.Add("Snooze until Tomorrow (8am)", null, (s, e) => _viewModel.SetDndUntilTomorrow());
        contextMenu.Items.Add(dndMenu);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Quit", null, (s, e) => QuitApplication());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => ShowAndRestore();
    }

    public void ShowAndRestore()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public bool IsExplicitQuit => _isExplicitQuit;

    public void ExplicitQuit()
    {
        _isExplicitQuit = true;
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        System.Windows.Application.Current?.Shutdown();
    }

    private void QuitApplication() => ExplicitQuit();

    private void MinimizeToTray()
    {
        Hide();
        MemoryOptimizer.TrimWorkingSet();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExplicitQuit && _configService.Config.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            MinimizeToTray();
        }
        else
        {
            if (!_isExplicitQuit)
            {
                ExplicitQuit();
            }
            base.OnClosing(e);
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _configService.Config.MinimizeToTrayOnClose)
        {
            MinimizeToTray();
        }
    }

    private bool _isSwitchingTab;
    private void SwitchTab(int index)
    {
        if (_isSwitchingTab) return;
        try
        {
            _isSwitchingTab = true;
            _viewModel.SelectedTabIndex = index;
            if (NavFeedRadio != null) NavFeedRadio.IsChecked = index == 0;
            if (NavAppsRadio != null) NavAppsRadio.IsChecked = index == 1;
            if (NavRulesRadio != null) NavRulesRadio.IsChecked = index == 2;
            if (NavSettingsRadio != null) NavSettingsRadio.IsChecked = index == 3;

            if (FeedPageView != null)
            {
                FeedPageView.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (index == 0) _viewModel.Feed.RefreshFromConfig();
            }
            if (AppsPageView != null) AppsPageView.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (RulesPageView != null) RulesPageView.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (SettingsPageView != null)
            {
                SettingsPageView.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
                if (index == 3) _viewModel.Settings.UseCompactFeedView = _configService.Config.UseCompactFeedView;
            }
        }
        finally
        {
            _isSwitchingTab = false;
        }
    }

    private void NavFeed_Checked(object sender, RoutedEventArgs e) => SwitchTab(0);
    private void NavApps_Checked(object sender, RoutedEventArgs e) => SwitchTab(1);
    private void NavRules_Checked(object sender, RoutedEventArgs e) => SwitchTab(2);
    private void NavSettings_Checked(object sender, RoutedEventArgs e) => SwitchTab(3);

    private void DndButton_Click(object sender, RoutedEventArgs e)
    {
        DndFlyout.IsOpen = !DndFlyout.IsOpen;
    }

    private void DndDisable_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DisableDnd();
        DndFlyout.IsOpen = false;
    }

    private void DndIndefinite_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetDndIndefinite();
        DndFlyout.IsOpen = false;
    }

    private void DndSnooze30m_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetDnd(TimeSpan.FromMinutes(30));
        DndFlyout.IsOpen = false;
    }

    private void DndSnooze1h_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetDnd(TimeSpan.FromHours(1));
        DndFlyout.IsOpen = false;
    }

    private void DndSnooze4h_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetDnd(TimeSpan.FromHours(4));
        DndFlyout.IsOpen = false;
    }

    private void DndSnoozeTomorrow_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SetDndUntilTomorrow();
        DndFlyout.IsOpen = false;
    }
}

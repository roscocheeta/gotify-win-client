using System.Windows;
using GotifyWin.Services;
using GotifyWin.ViewModels;

namespace GotifyWin;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configService = new ConfigService();
        configService.Load();

        var apiService = new GotifyApiService();
        var wsService = new GotifyWebSocketService();
        var filterEngine = new FilterEngine();
        var dndManager = new DndManager();
        var soundService = new SoundService();
        var presenter = new NotificationPresenter(soundService);
        var startupManager = new StartupManager();
        var patternAnalyzer = new NotificationPatternAnalyzer();
        var themeManager = new ThemeManager();
        themeManager.ApplyTheme(configService.Config.Theme);

        var mainViewModel = new MainViewModel(
            configService,
            apiService,
            wsService,
            filterEngine,
            dndManager,
            presenter,
            startupManager,
            patternAnalyzer,
            themeManager
        );

        _mainWindow = new MainWindow(mainViewModel, configService);

        var startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                             || configService.Config.StartMinimized;

        if (!startMinimized)
        {
            _mainWindow.Show();
        }
    }
}

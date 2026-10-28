using System.IO;
using System.Net.Http;
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

        AumidHelper.Initialize();

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

        var updateService = new UpdateService();
        updateService.CleanUpOldExecutables();

        SecureImageLoader.PolicyProvider = () =>
            (configService.Config.ServerUrl, configService.Config.Presentation.AllowExternalImages);

        var mainViewModel = new MainViewModel(
            configService,
            apiService,
            wsService,
            filterEngine,
            dndManager,
            presenter,
            startupManager,
            patternAnalyzer,
            themeManager,
            updateService
        );

        _mainWindow = new MainWindow(mainViewModel, configService);

        var isShowcase = e.Args.Any(a => a.Equals("--generate-showcase", StringComparison.OrdinalIgnoreCase));
        if (isShowcase)
        {
            _mainWindow.Show();
            _ = Task.Run(async () =>
            {
                await Task.Delay(600);
                UiDispatcher.RunOnUI(async () =>
                {
                    await ShowcaseGenerator.GenerateAsync(_mainWindow, mainViewModel, themeManager, "assets/screenshots");
                    Shutdown();
                });
            });
            return;
        }

        var startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                             || configService.Config.StartMinimized;

        if (!startMinimized)
        {
            _mainWindow.Show();
        }

        if (configService.Config.AutoCheckForUpdates)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(4000);
                    await updateService.CheckForUpdatesAsync(isManual: false);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"[App] Background update check error: {ex.Message}");
                }
            });
        }
    }
}

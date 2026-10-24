using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.ViewModels;

namespace GotifyWin.Services;

public static class ShowcaseGenerator
{
    public static async Task GenerateAsync(MainWindow mainWindow, MainViewModel vm, ThemeManager themeManager, string outputDir)
    {
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // 1. Dark Mode Feed Screenshot (Detailed View)
        themeManager.ApplyTheme(AppThemeMode.Dark);
        vm.Feed.IsCompactView = false;
        vm.SelectedTabIndex = 0;

        // Presentation badges: Clean connected state & DND Off
        vm.ConnectionStatusText = "Connected";
        vm.ConnectionStatusColor = "#10B981";
        vm.IsDndActive = false;
        vm.DndStatusText = "DND: Off";
        vm.DndButtonLabel = "DND: Off";

        var mockMessages = new List<GotifyMessage>
        {
            new()
            {
                Id = 1,
                AppId = 10,
                ApplicationName = "Uptime Kuma",
                Title = "AdGuard Home is DOWN",
                Message = "Connection timed out after 5000ms. Response code: ECONNREFUSED",
                Priority = 8,
                Date = DateTime.UtcNow.AddMinutes(-2),
                Extras = new Dictionary<string, System.Text.Json.JsonElement>()
            },
            new()
            {
                Id = 2,
                AppId = 12,
                ApplicationName = "Home Assistant",
                Title = "Washing Machine Completed",
                Message = "Laundry cycle in basement has finished. Cycle duration: 52 minutes.",
                Priority = 4,
                Date = DateTime.UtcNow.AddMinutes(-14),
                Extras = new Dictionary<string, System.Text.Json.JsonElement>()
            },
            new()
            {
                Id = 3,
                AppId = 15,
                ApplicationName = "Sonarr",
                Title = "Episode Downloaded",
                Message = "The Simpsons - S35E08 - Ae Bonny Romance [1080p Web-DL]",
                Priority = 4,
                FilteredByRuleName = "Silence Sonarr Downloads",
                Date = DateTime.UtcNow.AddMinutes(-45),
                Extras = new Dictionary<string, System.Text.Json.JsonElement>()
            },
            new()
            {
                Id = 4,
                AppId = 20,
                ApplicationName = "Proxmox VE",
                Title = "VM 104 (Docker-Prod) Backup Succeeded",
                Message = "Backup archive vzdump-qemu-104.vma.zst created in 3m 42s (42.8 GB). Status: OK",
                Priority = 5,
                Date = DateTime.UtcNow.AddHours(-2),
                Extras = new Dictionary<string, System.Text.Json.JsonElement>()
            },
            new()
            {
                Id = 5,
                AppId = 25,
                ApplicationName = "Portainer",
                Title = "Container Auto-Healed",
                Message = "Container vaultwarden healthy after restart policy trigger.",
                Priority = 6,
                DuplicateCount = 2,
                Date = DateTime.UtcNow.AddHours(-4),
                Extras = new Dictionary<string, System.Text.Json.JsonElement>()
            }
        };

        vm.Feed.SetInitialMessages(mockMessages);
        await Task.Delay(400);

        var darkPath = Path.Combine(outputDir, "gotifywin-dark-feed.png");
        CaptureElement((FrameworkElement)mainWindow.Content, 980, 680, darkPath);

        // 2. Light Mode Rules Page Screenshot
        themeManager.ApplyTheme(AppThemeMode.Light);
        vm.SelectedTabIndex = 2;

        vm.ConnectionStatusText = "Connected";
        vm.ConnectionStatusColor = "#10B981";
        vm.IsDndActive = false;
        vm.DndStatusText = "DND: Off";
        vm.DndButtonLabel = "DND: Off";

        vm.Rules.IsConfiguredRulesExpanded = true;
        vm.Rules.IsRecommendationsExpanded = true;
        vm.Rules.IsAdvancedExpanded = false;

        if (vm.Rules.Rules.Count == 0)
        {
            vm.Rules.Rules.Add(new NotificationRule
            {
                Name = "Silence Sonarr Downloads",
                AppFilterId = 15,
                AppFilterName = "Sonarr",
                TargetField = RuleField.Title,
                Operator = RuleOperator.Contains,
                Pattern = "Episode Downloaded",
                Action = RuleAction.SilentLog,
                Enabled = true
            });
            vm.Rules.Rules.Add(new NotificationRule
            {
                Name = "Drop Scrape Timeouts",
                AppFilterId = 18,
                AppFilterName = "Prometheus",
                TargetField = RuleField.Message,
                Operator = RuleOperator.Contains,
                Pattern = "scrape timeout",
                Action = RuleAction.Drop,
                Enabled = true
            });
        }

        if (vm.Rules.Recommendations.Count == 0)
        {
            vm.Rules.SetRecommendations(new List<RuleRecommendation>
            {
                new()
                {
                    AppId = 10,
                    AppName = "Uptime Kuma",
                    Title = "Healthcheck Ping Flood",
                    Description = "Repeated ping status updates detected 14 times.",
                    SuggestedPattern = "ping response:",
                    SuggestedField = RuleField.Message,
                    SuggestedOperator = RuleOperator.Contains,
                    SuggestedAction = RuleAction.SilentLog,
                    MatchCount = 14
                },
                new()
                {
                    AppId = 22,
                    AppName = "TrueNAS",
                    Title = "ZFS Pool Status Notices",
                    Description = "Routine scrub reports matched 6 times.",
                    SuggestedPattern = "scrub completed",
                    SuggestedField = RuleField.Message,
                    SuggestedOperator = RuleOperator.Contains,
                    SuggestedAction = RuleAction.SilentLog,
                    MatchCount = 6
                }
            });
        }

        await Task.Delay(400);

        var lightPath = Path.Combine(outputDir, "gotifywin-light-rules.png");
        CaptureElement((FrameworkElement)mainWindow.Content, 980, 680, lightPath);
    }

    private static void CaptureElement(FrameworkElement element, int width, int height, string filePath)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new System.Windows.Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }
}

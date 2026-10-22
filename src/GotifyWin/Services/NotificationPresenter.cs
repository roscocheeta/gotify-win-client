using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Application = System.Windows.Application;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Views;

namespace GotifyWin.Services;

public class NotificationPresenter : INotificationPresenter
{
    private const string AppUserModelId = "GotifyWin.DesktopClient";
    private readonly ISoundService _soundService;
    private readonly IHudManager _hudManager;
    private bool _aumidInitialized;

    public event EventHandler<string>? UrlOpenRequested;

    public NotificationPresenter(ISoundService soundService, IHudManager? hudManager = null)
    {
        _soundService = soundService;
        _hudManager = hudManager ?? new HudManager();
        InitializeAumidShortcut();
    }

    private void InitializeAumidShortcut()
    {
        if (_aumidInitialized) return;
        try
        {
            var startMenuDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs"
            );
            var shortcutPath = Path.Combine(startMenuDir, "Gotify Desktop.lnk");

            if (!File.Exists(shortcutPath))
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType != null)
                    {
                        dynamic? shell = Activator.CreateInstance(shellType);
                        if (shell != null)
                        {
                            var shortcut = shell.CreateShortcut(shortcutPath);
                            shortcut.TargetPath = exePath;
                            shortcut.Description = "Gotify Desktop Client";
                            shortcut.Save();
                        }
                    }
                }
            }
            _aumidInitialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotificationPresenter] AUMID shortcut registration notice: {ex.Message}");
        }
    }

    public void Present(GotifyMessage message, GotifyClientConfig config)
    {
        // 1. Play Sound (if enabled)
        if (config.Presentation.PlaySound)
        {
            _soundService.PlayAlert(config.Presentation.CustomSoundFilePath);
        }

        // 2. Present according to mode
        var mode = config.Presentation.Mode;

        if (mode == NotificationMode.CustomHud || mode == NotificationMode.Both)
        {
            ShowCustomHud(message, config.Presentation);
        }

        if (mode == NotificationMode.NativeToast || mode == NotificationMode.Both)
        {
            ShowNativeToast(message);
        }
    }

    private void ShowCustomHud(GotifyMessage message, PresentationSettings settings)
    {
        _hudManager.ShowNotification(message, settings, OpenUrl);
    }

    // SEC-06: Strip invalid XML 1.0 characters and escape XML entities
    public static string CleanXmlString(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // XML 1.0 specification: Character := #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD]
        var chars = text.Where(ch =>
            ch == 0x09 || ch == 0x0A || ch == 0x0D ||
            (ch >= 0x20 && ch <= 0xD7FF) ||
            (ch >= 0xE000 && ch <= 0xFFFD)
        ).ToArray();

        var sanitized = new string(chars);
        return System.Security.SecurityElement.Escape(sanitized) ?? string.Empty;
    }

    // SEC-02 & SEC-06: Validates scheme before protocol execution and cleans XML
    private void ShowNativeToast(GotifyMessage message)
    {
        try
        {
            var toastXml = new XmlDocument();
            var title = CleanXmlString(message.DisplayTitle);
            var body = CleanXmlString(message.Message);
            var iconXml = "";

            if (!string.IsNullOrEmpty(message.ApplicationIconPath) && File.Exists(message.ApplicationIconPath))
            {
                var iconUri = CleanXmlString(new Uri(message.ApplicationIconPath).AbsoluteUri);
                iconXml = $"<image placement=\"appLogoOverride\" src=\"{iconUri}\" hint-crop=\"circle\"/>";
            }

            var actionXml = "";
            // SEC-02: Strictly validate that ClickUrl is a valid HTTP/HTTPS protocol before exposing in toast action
            if (message.HasClickUrl &&
                Uri.TryCreate(message.ClickUrl, UriKind.Absolute, out var clickUri) &&
                (clickUri.Scheme == Uri.UriSchemeHttp || clickUri.Scheme == Uri.UriSchemeHttps))
            {
                var safeUrl = CleanXmlString(message.ClickUrl);
                actionXml = $"<actions><action content=\"Open Link\" arguments=\"{safeUrl}\" activationType=\"protocol\"/></actions>";
            }

            var xmlString = $@"
<toast duration=""short"" scenario=""default"">
    <visual>
        <binding template=""ToastGeneric"">
            <text>{title}</text>
            <text>{body}</text>
            {iconXml}
        </binding>
    </visual>
    {actionXml}
    <audio silent=""true"" />
</toast>";

            toastXml.LoadXml(xmlString);
            var toast = new ToastNotification(toastXml);

            toast.Activated += (s, e) =>
            {
                if (message.HasClickUrl)
                {
                    OpenUrl(message.ClickUrl!);
                }
            };

            ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(toast);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotificationPresenter] Native toast display error: {ex.Message}");
        }
    }

    public void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            UrlOpenRequested?.Invoke(this, url);

            // SEC-02: Validate URL protocol for security before launching (only http/https allowed)
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NotificationPresenter] OpenUrl error: {ex.Message}");
        }
    }
}

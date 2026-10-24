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
    private const string AppUserModelId = AumidHelper.AppUserModelId;
    private readonly ISoundService _soundService;
    private readonly IHudManager _hudManager;

    public event EventHandler<string>? UrlOpenRequested;

    public NotificationPresenter(ISoundService soundService, IHudManager? hudManager = null)
    {
        _soundService = soundService;
        _hudManager = hudManager ?? new HudManager();
        AumidHelper.Initialize();
    }

    public void Present(GotifyMessage message, GotifyClientConfig config)
    {
        // 1. Play Sound (if enabled)
        if (config.Presentation.PlaySound)
        {
            _soundService.PlayNotification(config.Presentation.CustomSoundFilePath);
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

    // SEC-02 & SEC-06 & SEC-08: Validates scheme before protocol execution, cleans XML, and builds toast XML
    public static string GenerateToastXml(GotifyMessage message)
    {
        var title = CleanXmlString(message.DisplayTitle);
        var body = CleanXmlString(message.Message);
        var iconXml = "";

        if (!string.IsNullOrEmpty(message.ApplicationIconPath) && File.Exists(message.ApplicationIconPath))
        {
            var iconUri = CleanXmlString(new Uri(message.ApplicationIconPath).AbsoluteUri);
            iconXml = $"<image placement=\"appLogoOverride\" src=\"{iconUri}\" hint-crop=\"circle\"/>";
        }

        var actionXml = "";
        // SEC-02 & SEC-08: Strictly validate that ClickUrl is a valid HTTP/HTTPS protocol and display target host
        if (message.HasClickUrl &&
            Uri.TryCreate(message.ClickUrl, UriKind.Absolute, out var clickUri) &&
            (clickUri.Scheme == Uri.UriSchemeHttp || clickUri.Scheme == Uri.UriSchemeHttps))
        {
            var safeUrl = CleanXmlString(message.ClickUrl);
            var hostLabel = string.IsNullOrWhiteSpace(clickUri.Host) ? "Open Link" : $"Open {CleanXmlString(clickUri.Host)}";
            actionXml = $"<actions><action content=\"{hostLabel}\" arguments=\"{safeUrl}\" activationType=\"protocol\"/></actions>";
        }

        return $@"
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
    }

    private void ShowNativeToast(GotifyMessage message)
    {
        try
        {
            AumidHelper.EnsureShortcut();
            var toastXml = new XmlDocument();
            var xmlString = GenerateToastXml(message);
            toastXml.LoadXml(xmlString);
            var toast = new ToastNotification(toastXml);

            toast.Activated += (s, e) =>
            {
                if (message.HasClickUrl)
                {
                    OpenUrl(message.ClickUrl!);
                }
            };

            var notifier = ToastNotificationManager.CreateToastNotifier(AppUserModelId);
            notifier.Show(toast);
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

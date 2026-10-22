using GotifyWin.Models;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public interface INotificationPresenter
{
    void Present(GotifyMessage message, GotifyClientConfig config);
    void OpenUrl(string url);
    event EventHandler<string>? UrlOpenRequested;
}

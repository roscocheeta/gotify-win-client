using GotifyWin.Models;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public interface IGotifyWebSocketService : IDisposable
{
    ConnectionState State { get; }
    string? LastError { get; }

    event EventHandler<GotifyMessage>? MessageReceived;
    event EventHandler<ConnectionState>? ConnectionStateChanged;

    Task ConnectAsync(string serverUrl, string clientToken);
    Task DisconnectAsync();
}

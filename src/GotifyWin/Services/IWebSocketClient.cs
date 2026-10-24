using System.Net.WebSockets;

namespace GotifyWin.Services;

/// <summary>
/// Abstraction over WebSocket client operations to enable unit testing,
/// reconnection simulation, and OS network boundary contract verification.
/// </summary>
public interface IWebSocketClient : IDisposable
{
    WebSocketState State { get; }
    void SetRequestHeader(string headerName, string headerValue);
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
    Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);
    Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
}

/// <summary>
/// Production implementation backed by <see cref="ClientWebSocket"/>.
/// </summary>
public sealed class DefaultWebSocketClient : IWebSocketClient
{
    private readonly ClientWebSocket _ws = new();

    public WebSocketState State => _ws.State;

    public void SetRequestHeader(string headerName, string headerValue)
    {
        _ws.Options.SetRequestHeader(headerName, headerValue);
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
    }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        _ws.ConnectAsync(uri, cancellationToken);

    public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
        _ws.ReceiveAsync(buffer, cancellationToken);

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        _ws.CloseAsync(closeStatus, statusDescription, cancellationToken);

    public void Dispose() => _ws.Dispose();
}

using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using Xunit;

namespace GotifyWin.Tests;

public class GotifyWebSocketServiceTests
{
    private class FakeWebSocketClient : IWebSocketClient
    {
        public WebSocketState State { get; set; } = WebSocketState.None;
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Uri? ConnectedUri { get; private set; }
        public bool IsClosed { get; private set; }
        public bool IsDisposed { get; private set; }

        private readonly Func<ArraySegment<byte>, CancellationToken, Task<WebSocketReceiveResult>>? _receiveHandler;
        private readonly Func<Uri, CancellationToken, Task>? _connectHandler;

        public FakeWebSocketClient(
            Func<ArraySegment<byte>, CancellationToken, Task<WebSocketReceiveResult>>? receiveHandler = null,
            Func<Uri, CancellationToken, Task>? connectHandler = null)
        {
            _receiveHandler = receiveHandler;
            _connectHandler = connectHandler;
        }

        public void SetRequestHeader(string headerName, string headerValue)
        {
            Headers[headerName] = headerValue;
        }

        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            ConnectedUri = uri;
            if (_connectHandler != null)
            {
                await _connectHandler(uri, cancellationToken);
            }
            State = WebSocketState.Open;
        }

        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_receiveHandler != null)
            {
                return _receiveHandler(buffer, cancellationToken);
            }
            // Default: hang or wait for cancellation
            var tcs = new TaskCompletionSource<WebSocketReceiveResult>();
            cancellationToken.Register(() => tcs.TrySetCanceled());
            return tcs.Task;
        }

        public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            IsClosed = true;
            State = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            IsDisposed = true;
            State = WebSocketState.Closed;
        }
    }

    [Fact]
    public async Task ConnectAsync_InvalidArguments_ThrowsArgumentException()
    {
        using var service = new GotifyWebSocketService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.ConnectAsync("", "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ConnectAsync("https://example.com", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ConnectAsync("ftp://example.com", "token"));
    }

    [Fact]
    public async Task ConnectAsync_SuccessfulConnection_SetsHeadersAndConnects()
    {
        FakeWebSocketClient? capturedClient = null;
        var connectedTcs = new TaskCompletionSource<bool>();

        using var service = new GotifyWebSocketService(() =>
        {
            capturedClient = new FakeWebSocketClient(connectHandler: (uri, ct) =>
            {
                connectedTcs.TrySetResult(true);
                return Task.CompletedTask;
            });
            return capturedClient;
        });

        await service.ConnectAsync("https://gotify.example.com", "my-secret-token");

        var completed = await Task.WhenAny(connectedTcs.Task, Task.Delay(2000));
        Assert.Equal(connectedTcs.Task, completed);

        Assert.NotNull(capturedClient);
        Assert.Equal("my-secret-token", capturedClient!.Headers["X-Gotify-Key"]);
        Assert.Equal("wss://gotify.example.com/stream", capturedClient.ConnectedUri?.ToString());
    }

    [Fact]
    public async Task ReceiveLoop_ValidMessage_FiresMessageReceivedEvent()
    {
        var sampleMessage = new GotifyMessage
        {
            Id = 42,
            AppId = 1,
            Title = "Server Alert",
            Message = "CPU temperature is high",
            Priority = 8
        };
        var json = JsonSerializer.Serialize(sampleMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        var receiveCount = 0;
        var messageReceivedTcs = new TaskCompletionSource<GotifyMessage>();

        using var service = new GotifyWebSocketService(() =>
        {
            return new FakeWebSocketClient(receiveHandler: (buffer, ct) =>
            {
                receiveCount++;
                if (receiveCount == 1)
                {
                    Array.Copy(bytes, 0, buffer.Array!, buffer.Offset, bytes.Length);
                    return Task.FromResult(new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true));
                }

                // Hang subsequent receives
                var tcs = new TaskCompletionSource<WebSocketReceiveResult>();
                ct.Register(() => tcs.TrySetCanceled());
                return tcs.Task;
            });
        });

        service.MessageReceived += (s, msg) => messageReceivedTcs.TrySetResult(msg);

        await service.ConnectAsync("https://gotify.example.com", "token");

        var completed = await Task.WhenAny(messageReceivedTcs.Task, Task.Delay(3000));
        Assert.Equal(messageReceivedTcs.Task, completed);

        var received = await messageReceivedTcs.Task;
        Assert.Equal(42, received.Id);
        Assert.Equal("Server Alert", received.Title);
        Assert.Equal("CPU temperature is high", received.Message);
        Assert.Equal(8, received.Priority);
    }

    [Fact]
    public async Task ReceiveLoop_CloseMessage_ExitsCleanly()
    {
        using var service = new GotifyWebSocketService(() =>
        {
            return new FakeWebSocketClient(receiveHandler: (buffer, ct) =>
            {
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "Closing"));
            });
        });

        var stateChangedToReconnecting = new TaskCompletionSource<bool>();
        service.ConnectionStateChanged += (s, state) =>
        {
            if (state == ConnectionState.Reconnecting)
            {
                stateChangedToReconnecting.TrySetResult(true);
            }
        };

        await service.ConnectAsync("https://gotify.example.com", "token");

        // When a close frame is received, the receive loop exits and the connection loop transitions to Reconnecting
        var completed = await Task.WhenAny(stateChangedToReconnecting.Task, Task.Delay(3000));
        Assert.Equal(stateChangedToReconnecting.Task, completed);
    }

    [Fact]
    public async Task DisconnectAsync_StopsServiceAndSetsStateDisconnected()
    {
        FakeWebSocketClient? client = null;

        using var service = new GotifyWebSocketService(() =>
        {
            client = new FakeWebSocketClient();
            return client;
        });

        await service.ConnectAsync("https://gotify.example.com", "token");
        await Task.Delay(100);

        await service.DisconnectAsync();
        Assert.Equal(ConnectionState.Disconnected, service.State);
    }

    [Fact]
    public async Task ConnectionLoop_WhenConnectFails_TransitionsToReconnecting()
    {
        var reconnectingTcs = new TaskCompletionSource<bool>();

        using var service = new GotifyWebSocketService(() =>
        {
            return new FakeWebSocketClient(connectHandler: (uri, ct) =>
            {
                throw new WebSocketException("Host unreachable");
            });
        });

        service.ConnectionStateChanged += (s, state) =>
        {
            if (state == ConnectionState.Reconnecting)
            {
                reconnectingTcs.TrySetResult(true);
            }
        };

        await service.ConnectAsync("https://gotify.example.com", "token");

        var completed = await Task.WhenAny(reconnectingTcs.Task, Task.Delay(3000));
        Assert.Equal(reconnectingTcs.Task, completed);
        Assert.Equal(ConnectionState.Reconnecting, service.State);
        Assert.Contains("Host unreachable", service.LastError);
    }
}

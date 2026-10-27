using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GotifyWin.Models;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public class GotifyWebSocketService : IGotifyWebSocketService
{
    private const int MaxMessageSizeBytes = 512 * 1024; // SEC-04: 512 KB ceiling to prevent LOH exhaustion DoS
    private readonly SemaphoreSlim _connectionLock = new(1, 1); // SEC-12: Thread synchronization
    private readonly Func<IWebSocketClient> _webSocketFactory;
    private IWebSocketClient? _webSocket;
    private CancellationTokenSource? _cts;
    private string? _serverUrl;
    private string? _clientToken;
    private bool _isDisposed;
    private bool _explicitDisconnect;

    public GotifyWebSocketService(Func<IWebSocketClient>? webSocketFactory = null)
    {
        _webSocketFactory = webSocketFactory ?? (() => new DefaultWebSocketClient());
        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch
        {
            // Session 0 or headless runner might not support SystemEvents
        }
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume && !_explicitDisconnect && !string.IsNullOrEmpty(_serverUrl) && !string.IsNullOrEmpty(_clientToken))
        {
            System.Diagnostics.Debug.WriteLine("[GotifyWebSocket] System resumed from sleep/standby. Reconnecting...");
            _ = Task.Run(async () =>
            {
                try
                {
                    await ConnectAsync(_serverUrl, _clientToken);
                }
                catch { }
            });
        }
    }

    private ConnectionState _state = ConnectionState.Disconnected;
    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                ConnectionStateChanged?.Invoke(this, _state);
            }
        }
    }

    public string? LastError { get; private set; }

    public event EventHandler<GotifyMessage>? MessageReceived;
    public event EventHandler<ConnectionState>? ConnectionStateChanged;

    public async Task ConnectAsync(string serverUrl, string clientToken)
    {
        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(clientToken))
            throw new ArgumentException("Server URL and Client Token must not be empty.");

        var validation = ServerUrlValidator.Validate(serverUrl);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error);

        await _connectionLock.WaitAsync();
        try
        {
            _serverUrl = serverUrl.Trim().TrimEnd('/');
            _clientToken = clientToken.Trim();
            _explicitDisconnect = false;

            await StopInternalAsync();

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ConnectionLoopAsync(_cts.Token));
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            _explicitDisconnect = true;
            await StopInternalAsync();
            State = ConnectionState.Disconnected;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task StopInternalAsync()
    {
        try
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose(); // SEC-12: Dispose previous CTS
                _cts = null;
            }

            if (_webSocket != null)
            {
                if (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.Connecting)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", closeCts.Token);
                    }
                    catch { }
                }
                _webSocket.Dispose();
                _webSocket = null;
            }
        }
        catch { }
    }

    private async Task ConnectionLoopAsync(CancellationToken ct)
    {
        var backoffSeconds = 2.0;
        var maxBackoffSeconds = 60.0;

        while (!ct.IsCancellationRequested && !_explicitDisconnect)
        {
            try
            {
                State = ConnectionState.Connecting;
                LastError = null;

                var wsBase = _serverUrl!
                    .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
                    .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);

                _webSocket = _webSocketFactory();

                // SEC: Authenticate via header only so the token never appears in URLs (proxy/access logs).
                // Query-string token is used solely as a fallback if the header cannot be set.
                var useQueryFallback = false;
                try
                {
                    _webSocket.SetRequestHeader("X-Gotify-Key", _clientToken!);
                }
                catch
                {
                    useQueryFallback = true;
                }

                var wsUri = useQueryFallback
                    ? new Uri($"{wsBase}/stream?token={Uri.EscapeDataString(_clientToken!)}")
                    : new Uri($"{wsBase}/stream");
                await _webSocket.ConnectAsync(wsUri, ct);

                State = ConnectionState.Connected;
                backoffSeconds = 2.0; // Reset backoff on successful connect

                await ReceiveLoopAsync(_webSocket, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                System.Diagnostics.Debug.WriteLine($"[GotifyWebSocket] Connection error: {ex.Message}");
            }

            if (ct.IsCancellationRequested || _explicitDisconnect)
                break;

            State = ConnectionState.Reconnecting;

            // Exponential backoff with jitter
            var jitter = System.Security.Cryptography.RandomNumberGenerator.GetInt32(-500, 501) / 1000.0;
            var delay = TimeSpan.FromSeconds(Math.Min(backoffSeconds + jitter, maxBackoffSeconds));
            backoffSeconds = Math.Min(backoffSeconds * 2, maxBackoffSeconds);

            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        State = ConnectionState.Disconnected;
    }

    private async Task ReceiveLoopAsync(IWebSocketClient ws, CancellationToken ct)
    {
        var buffer = new byte[1024 * 8];

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                // SEC-04: Limit frame size to prevent LOH memory exhaustion attacks
                if (ms.Length + result.Count > MaxMessageSizeBytes)
                {
                    System.Diagnostics.Debug.WriteLine($"[GotifyWebSocket] Discarded oversized WebSocket message exceeding {MaxMessageSizeBytes} bytes.");
                    break;
                }

                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Text && ms.Length <= MaxMessageSizeBytes)
            {
                ms.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(ms, Encoding.UTF8);
                var json = await reader.ReadToEndAsync(ct);

                try
                {
                    var message = JsonSerializer.Deserialize<GotifyMessage>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (message != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GotifyWebSocket] Received message #{message.Id}");
                        MessageReceived?.Invoke(this, message);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GotifyWebSocket] Failed to parse message JSON: {ex.Message}");
                }
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _explicitDisconnect = true;

        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch { }

        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        _webSocket?.Dispose();
        _connectionLock.Dispose();
    }
}

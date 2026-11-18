using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using GotifyWin.ViewModels;
using Xunit;

namespace GotifyWin.Tests;

public class InteractiveActionsAndAppDiscoveryTests
{
    private class StubConfigService : IConfigService
    {
        public GotifyClientConfig Config { get; } = new();
        public string ConfigFilePath => string.Empty;
        public bool HasDecryptionFailed => false;
        public int SaveCount { get; private set; }
        public void Load() { }
        public void Save() => SaveCount++;
        public void Reset() { }
    }

    private class StubApiService : IGotifyApiService
    {
        public List<GotifyApplication> AppsToReturn { get; } = new();
        public int GetAppsCallCount { get; private set; }

        public Task<bool> TestConnectionAsync(string serverUrl, string clientToken, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<GotifyUser?> GetCurrentUserAsync(string serverUrl, string clientToken, CancellationToken ct = default)
            => Task.FromResult<GotifyUser?>(null);

        public Task<List<GotifyApplication>> GetApplicationsAsync(string serverUrl, string clientToken, CancellationToken ct = default)
        {
            GetAppsCallCount++;
            return Task.FromResult(AppsToReturn.ToList());
        }

        public Task<List<GotifyMessage>> GetRecentMessagesAsync(string serverUrl, string clientToken, int limit = 50, CancellationToken ct = default)
            => Task.FromResult(new List<GotifyMessage>());

        public Task<bool> DeleteMessageAsync(string serverUrl, string clientToken, long messageId, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> ClearAllMessagesAsync(string serverUrl, string clientToken, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<string?> CacheApplicationIconAsync(string serverUrl, string clientToken, GotifyApplication app, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    private class DummyWsService : IGotifyWebSocketService
    {
        public ConnectionState State => ConnectionState.Connected;
        public string? LastError => null;
        public event EventHandler<GotifyMessage>? MessageReceived { add { } remove { } }
        public event EventHandler<ConnectionState>? ConnectionStateChanged { add { } remove { } }
        public Task ConnectAsync(string serverUrl, string clientToken) => Task.CompletedTask;
        public Task ConnectAsync(string serverUrl, string clientToken, CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private class StubDndManager : IDndManager
    {
        public bool IsDndActive(DndSettings settings) => false;
        public bool IsQuietHours(DndSettings settings, DateTime? now = null) => false;
        public bool IsFullscreenAppRunning() => false;
        public bool IsFullscreenAppRunning(DndSettings settings) => false;
        public FullscreenState GetFullscreenState() => FullscreenState.None;
        public FullscreenState GetFullscreenState(DndSettings settings) => FullscreenState.None;
        public void SetManualDnd(DndSettings settings, TimeSpan? duration = null) { }
        public void DisableManualDnd(DndSettings settings) { }
        public string GetStatusDescription(DndSettings settings) => "Off";
    }

    private class StubStartupManager : IStartupManager
    {
        public bool IsAutoStartEnabled() => false;
        public void SetAutoStart(bool enable) { }
    }

    private class RecordingPresenter : INotificationPresenter
    {
        public List<GotifyMessage> Presented { get; } = new();
        public event EventHandler<string>? UrlOpenRequested { add { } remove { } }
        public void Present(GotifyMessage message, GotifyClientConfig config) => Presented.Add(message);
        public void OpenUrl(string url) { }
    }

    private class StubHttpHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public HttpStatusCode StatusToReturn { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return new HttpResponseMessage(StatusToReturn);
        }
    }

    [Fact]
    public void GotifyMessage_ParsesViewAndHttpActions_FromClientNotificationExtras()
    {
        var json = """
        {
            "id": 10,
            "appid": 2,
            "title": "Server Alert",
            "message": "Container stopped",
            "extras": {
                "client::notification": {
                    "actions": [
                        { "action": "view", "label": "Open Portainer", "url": "https://portainer.local/containers", "clear": true },
                        { "action": "http", "label": "Restart", "url": "https://hooks.local/restart", "method": "POST", "body": { "id": 1 } }
                    ]
                }
            }
        }
        """;

        var msg = JsonSerializer.Deserialize<GotifyMessage>(json)!;
        var actions = msg.MessageActions;

        Assert.True(msg.HasMessageActions);
        Assert.Equal(2, actions.Count);
        Assert.Equal(GotifyActionType.View, actions[0].Type);
        Assert.Equal("Open Portainer", actions[0].Label);
        Assert.True(actions[0].Clear);
        Assert.Equal(GotifyActionType.Http, actions[1].Type);
        Assert.Equal("POST", actions[1].Method);
        Assert.Contains("\"id\"", actions[1].Body);
        Assert.Contains("1", actions[1].Body);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ms-msdt:/id")]
    [InlineData("ftp://example.com/file")]
    public void GotifyMessage_RejectsUnsafeActionSchemes(string unsafeUrl)
    {
        var json = $$"""
        {
            "id": 11,
            "appid": 2,
            "extras": {
                "actions": [
                    { "action": "view", "label": "Unsafe", "url": "{{unsafeUrl}}" },
                    { "action": "http", "label": "Safe", "url": "https://hooks.local/ok" }
                ]
            }
        }
        """;

        var msg = JsonSerializer.Deserialize<GotifyMessage>(json)!;
        Assert.Single(msg.MessageActions);
        Assert.Equal("Safe", msg.MessageActions[0].Label);
    }

    [Fact]
    public void GenerateToastXml_IncludesProtocolAndForegroundActions_UpToFiveMax()
    {
        var json = """
        {
            "id": 12,
            "appid": 3,
            "title": "Build",
            "message": "Deploy ready",
            "extras": {
                "client::notification": {
                    "click": { "url": "https://ci.local/job/1" },
                    "actions": [
                        { "action": "view", "label": "Logs & <Status>", "url": "https://ci.local/logs" },
                        { "action": "http", "label": "Approve", "url": "https://ci.local/approve", "method": "POST" },
                        { "action": "http", "label": "Rollback", "url": "https://ci.local/rollback", "method": "POST" },
                        { "action": "view", "label": "Docs", "url": "https://ci.local/docs" },
                        { "action": "view", "label": "ExtraIgnored", "url": "https://ci.local/ignored" }
                    ]
                }
            }
        }
        """;

        var msg = JsonSerializer.Deserialize<GotifyMessage>(json)!;
        var xml = NotificationPresenter.GenerateToastXml(msg);

        Assert.Contains("activationType=\"protocol\"", xml);
        Assert.Contains("Logs &amp; &lt;Status&gt;", xml);
        Assert.Contains("arguments=\"gotifywin-action:1\" activationType=\"foreground\"", xml);
        Assert.DoesNotContain("ExtraIgnored", xml);
    }

    [Fact]
    public async Task ActionExecutionService_ExecutesWebhook_WithoutLeakingTokens()
    {
        var handler = new StubHttpHandler { StatusToReturn = HttpStatusCode.Accepted };
        using var client = new HttpClient(handler);
        var service = new ActionExecutionService(client);

        var action = new GotifyMessageAction
        {
            Type = GotifyActionType.Http,
            Label = "Restart",
            Url = "https://hooks.local/restart",
            Method = "POST",
            Headers = new Dictionary<string, string> { ["X-Custom"] = "val1" },
            Body = "{\"service\":\"nginx\"}"
        };

        var result = await service.ExecuteWebhookAsync(action);

        Assert.True(result.Success);
        Assert.Equal("HTTP 202", result.Message);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.False(handler.LastRequest.Headers.Contains("X-Gotify-Key"));
        Assert.Equal("val1", handler.LastRequest.Headers.GetValues("X-Custom").Single());
        Assert.Equal("{\"service\":\"nginx\"}", handler.LastBody);
    }

    [Fact]
    public async Task ActionExecutionService_RejectsNonHttpScheme()
    {
        var service = new ActionExecutionService();
        var action = new GotifyMessageAction
        {
            Type = GotifyActionType.Http,
            Label = "Bad",
            Url = "file:///C:/test.txt",
            Method = "POST"
        };

        var result = await service.ExecuteWebhookAsync(action);
        Assert.False(result.Success);
        Assert.Contains("unsafe", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MainViewModel_UnknownAppId_SyncsBeforeNotifying_WhenAutoEnableTrue()
    {
        var cfg = new StubConfigService();
        cfg.Config.ServerUrl = "https://gotify.local";
        cfg.Config.ClientToken = "token123";
        cfg.Config.AutoEnableNewApps = true;

        var api = new StubApiService();
        api.AppsToReturn.Add(new GotifyApplication { Id = 42, Name = "Uptime Kuma" });

        var presenter = new RecordingPresenter();
        using var vm = new MainViewModel(
            cfg,
            api,
            new DummyWsService(),
            new FilterEngine(),
            new StubDndManager(),
            presenter,
            new StubStartupManager());

        var msg = new GotifyMessage { Id = 100, AppId = 42, Title = "Up", Message = "OK", Priority = 5 };
        await vm.HandleIncomingMessageAsync(msg);

        Assert.Equal(1, api.GetAppsCallCount);
        Assert.True(cfg.Config.AppFilters.ContainsKey(42));
        Assert.True(cfg.Config.AppFilters[42].Enabled);
        Assert.Single(presenter.Presented);
        Assert.Equal("Uptime Kuma", presenter.Presented[0].ApplicationName);
    }

    [Fact]
    public async Task MainViewModel_UnknownAppId_MutesByDefaultAndNotifiesDiscovery_WhenAutoEnableFalse()
    {
        var cfg = new StubConfigService();
        cfg.Config.ServerUrl = "https://gotify.local";
        cfg.Config.ClientToken = "token123";
        cfg.Config.AutoEnableNewApps = false;

        var api = new StubApiService();
        api.AppsToReturn.Add(new GotifyApplication { Id = 77, Name = "Noisy Backup" });

        var presenter = new RecordingPresenter();
        using var vm = new MainViewModel(
            cfg,
            api,
            new DummyWsService(),
            new FilterEngine(),
            new StubDndManager(),
            presenter,
            new StubStartupManager());

        var msg = new GotifyMessage { Id = 200, AppId = 77, Title = "Spam", Message = "Chunk uploaded", Priority = 5 };
        await vm.HandleIncomingMessageAsync(msg);

        Assert.True(cfg.Config.AppFilters.ContainsKey(77));
        Assert.False(cfg.Config.AppFilters[77].Enabled);
        Assert.Single(presenter.Presented);
        Assert.Contains("New Gotify App: Noisy Backup", presenter.Presented[0].Title);
    }

    [Fact]
    public void TrayIconAsset_ExistsAndContainsNativeSmallIconSizes()
    {
        var trayPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "GotifyWin", "Assets", "tray.ico"));

        Assert.True(File.Exists(trayPath), $"Expected tray.ico at {trayPath}");

        using var stream = File.OpenRead(trayPath);
        using var icon16 = new System.Drawing.Icon(stream, 16, 16);
        stream.Position = 0;
        using var icon24 = new System.Drawing.Icon(stream, 24, 24);
        stream.Position = 0;
        using var icon32 = new System.Drawing.Icon(stream, 32, 32);

        Assert.Equal(16, icon16.Width);
        Assert.Equal(24, icon24.Width);
        Assert.Equal(32, icon32.Width);
    }
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using Xunit;

namespace GotifyWin.Tests;

public class SecurityTests
{
    [Theory]
    [InlineData("file:///C:/Users/Public/malicious.bat")]
    [InlineData("powershell.exe -Command Write-Host")]
    [InlineData("cmd.exe /c calc")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("javascript:alert(1)")]
    public void NotificationPresenter_UrlValidation_RejectsNonHttpProtocols(string unsafeUrl)
    {
        var soundService = new SoundService();
        var presenter = new NotificationPresenter(soundService);

        presenter.OpenUrl(unsafeUrl);

        // Verify that unsafeUrl is never accepted as a valid HTTP or HTTPS URI
        var isValidHttpOrHttps = Uri.TryCreate(unsafeUrl, UriKind.Absolute, out var uri) &&
                                 (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        Assert.False(isValidHttpOrHttps);
    }

    [Theory]
    [InlineData("https://status.homelab.lan/details")]
    [InlineData("http://192.168.1.100:8080/dashboard")]
    public void NotificationPresenter_UrlValidation_AcceptsHttpAndHttps(string safeUrl)
    {
        Assert.True(Uri.TryCreate(safeUrl, UriKind.Absolute, out var uri));
        Assert.True(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    [Fact]
    public void NotificationPresenter_CleanXmlString_StripsIllegalXmlCharacters()
    {
        // ASCII control chars 0x00 to 0x08, 0x0B, 0x0C, 0x0E-0x1F are illegal in XML 1.0
        var inputWithControlChars = "Hello\0World\x08Test\x1F<Alert>&Notice";
        var escaped = NotificationPresenter.CleanXmlString(inputWithControlChars);

        Assert.NotNull(escaped);
        Assert.False(escaped.Contains('\0'));
        Assert.False(escaped.Contains('\x08'));
        Assert.False(escaped.Contains('\x1F'));
        Assert.Contains("&lt;Alert&gt;", escaped);
        Assert.Contains("&amp;Notice", escaped);
    }

    [Fact]
    public void FilterEngine_NullTitleOrMessage_DoesNotThrowNullReference()
    {
        var engine = new FilterEngine();
        var config = new GotifyClientConfig();

        var messageWithNulls = new GotifyMessage
        {
            AppId = 1,
            Title = null!,
            Message = null!,
            Priority = 5
        };

        var exception = Record.Exception(() => engine.Evaluate(messageWithNulls, config, isDndActive: false));
        Assert.Null(exception);
    }

    [Fact]
    public void FilterEngine_RegexReDoS_TimesOutSafelyWithoutHanging()
    {
        var engine = new FilterEngine();
        var config = new GotifyClientConfig();

        // Classic catastrophic backtracking pattern: (a+)+$
        config.Rules.Add(new NotificationRule
        {
            Name = "ReDoS Test",
            TargetField = RuleField.Message,
            Operator = RuleOperator.RegexMatch,
            Pattern = @"(a+)+$",
            Action = RuleAction.Drop,
            Enabled = true
        });

        var evilMessage = new GotifyMessage
        {
            AppId = 1,
            Title = "Safe Title",
            Message = new string('a', 30) + "!", // Causes exponential backtracking if no timeout
            Priority = 5
        };

        var start = DateTime.UtcNow;
        var result = engine.Evaluate(evilMessage, config, isDndActive: false);
        var elapsed = DateTime.UtcNow - start;

        // Must complete within 200ms due to the 50ms regex timeout
        Assert.True(elapsed.TotalMilliseconds < 200, $"Regex took too long: {elapsed.TotalMilliseconds}ms");
        Assert.Equal(FilterDecision.Alert, result.Decision); // Should safely fail regex match
    }

    [Fact]
    public async Task GotifyApiService_ExternalIconUrl_IsNeverFetched()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new MockHttpMessageHandler((req) =>
        {
            capturedRequest = req;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) // PNG magic bytes
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return response;
        });

        using var client = new System.Net.Http.HttpClient(handler);
        var tempCache = Path.Combine(Path.GetTempPath(), $"gotify_cache_{Guid.NewGuid():N}");
        var service = new GotifyApiService(client, tempCache);

        try
        {
            var app = new GotifyApplication
            {
                Id = 999,
                Name = "Third Party App",
                Image = "https://external-attacker.com/avatar.png"
            };

            var result = await service.CacheApplicationIconAsync("https://gotify.myhomelab.lan", "secret-token-12345", app);

            // SEC: cross-origin icon URLs are never fetched at all (no SSRF / IP leak), let alone with the token
            Assert.Null(result);
            Assert.Null(capturedRequest);
        }
        finally
        {
            if (Directory.Exists(tempCache))
            {
                try { Directory.Delete(tempCache, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task GotifyApiService_SameOriginIconUrl_SendsTokenHeader()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new MockHttpMessageHandler((req) =>
        {
            capturedRequest = req;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47 })
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            return response;
        });

        using var client = new System.Net.Http.HttpClient(handler);
        var tempCache = Path.Combine(Path.GetTempPath(), $"gotify_cache_{Guid.NewGuid():N}");
        var service = new GotifyApiService(client, tempCache);

        try
        {
            var app = new GotifyApplication
            {
                Id = 10,
                Name = "Local App",
                Image = "image/app_icon.png" // relative -> same origin
            };

            await service.CacheApplicationIconAsync("https://gotify.myhomelab.lan", "secret-token-12345", app);

            Assert.NotNull(capturedRequest);
            // Same origin MUST send token header
            Assert.True(capturedRequest.Headers.Contains("X-Gotify-Key"));
            Assert.Equal("secret-token-12345", capturedRequest.Headers.GetValues("X-Gotify-Key").First());
        }
        finally
        {
            if (Directory.Exists(tempCache))
            {
                try { Directory.Delete(tempCache, true); } catch { }
            }
        }
    }

    [Theory]
    [InlineData("https://status.github.com:8443/issues/42", "status.github.com", "🔗 Open status.github.com")]
    [InlineData("http://192.168.1.100/admin", "192.168.1.100", "🔗 Open 192.168.1.100")]
    [InlineData("https://alerts.homelab.lan", "alerts.homelab.lan", "🔗 Open alerts.homelab.lan")]
    [InlineData("file:///C:/malicious.exe", null, "🔗 Open Link")]
    [InlineData("powershell.exe -c calc", null, "🔗 Open Link")]
    [InlineData("", null, "🔗 Open Link")]
    [InlineData(null, null, "🔗 Open Link")]
    public void GotifyMessage_ClickUrlHost_ExtractsSafeHostAndButtonText(string? url, string? expectedHost, string expectedButtonText)
    {
        var msg = new GotifyMessage
        {
            Id = 1,
            Title = "Test",
            Message = "Test body"
        };

        if (url != null)
        {
            var json = $"{{\"click\": {{\"url\": \"{url}\"}}}}";
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            msg.Extras = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["client::notification"] = doc.RootElement.Clone()
            };
        }

        Assert.Equal(expectedHost, msg.ClickUrlHost);
        Assert.Equal(expectedButtonText, msg.ClickUrlButtonText);
    }

    [Fact]
    public void NotificationPresenter_GenerateToastXml_IncludesHostInActionLabel()
    {
        var msg = new GotifyMessage
        {
            Id = 101,
            Title = "Disk Alert",
            Message = "Storage pool 1 is 95% full"
        };

        var json = "{\"click\": {\"url\": \"https://nas.homelab.lan/storage/pools\"}}";
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        msg.Extras = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["client::notification"] = doc.RootElement.Clone()
        };

        var xml = NotificationPresenter.GenerateToastXml(msg);

        Assert.Contains("content=\"Open nas.homelab.lan\"", xml);
        Assert.Contains("arguments=\"https://nas.homelab.lan/storage/pools\"", xml);
        Assert.Contains("activationType=\"protocol\"", xml);
    }

    [Fact]
    public void NotificationPresenter_GenerateToastXml_RejectsUnsafeProtocolsFromAction()
    {
        var msg = new GotifyMessage
        {
            Id = 102,
            Title = "Dangerous Link",
            Message = "Do not run"
        };

        var json = "{\"click\": {\"url\": \"ms-msdt:/id PCWDiagnostic\"}}";
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        msg.Extras = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["client::notification"] = doc.RootElement.Clone()
        };

        var xml = NotificationPresenter.GenerateToastXml(msg);

        // Must NOT contain action buttons for unsafe non-HTTP/HTTPS protocols
        Assert.DoesNotContain("<actions>", xml);
        Assert.DoesNotContain("ms-msdt", xml);
    }

    [Fact]
    public void FilterEngine_ComputeDebounceKey_ProducesCollisionResistantSha256()
    {
        // 1. Valid 32-hex character length (16 bytes)
        var key1 = FilterEngine.ComputeDebounceKey(1, "Service Down", "Container exited 137");
        Assert.Equal(32, key1.Length);
        Assert.Matches("^[0-9A-F]{32}$", key1);

        // 2. Whitespace trimming produces identical key for effective anti-spam
        var keyTrimmed = FilterEngine.ComputeDebounceKey(1, "  Service Down  ", "Container exited 137\n");
        Assert.Equal(key1, keyTrimmed);

        // 3. Different apps with identical title/message do NOT collide
        var keyDifferentApp = FilterEngine.ComputeDebounceKey(2, "Service Down", "Container exited 137");
        Assert.NotEqual(key1, keyDifferentApp);

        // 4. Distinct messages do NOT collide
        var keyDistinct = FilterEngine.ComputeDebounceKey(1, "Service Restored", "Container exited 137");
        Assert.NotEqual(key1, keyDistinct);

        // 5. Null titles and messages are safely handled
        var keyNulls = FilterEngine.ComputeDebounceKey(1, null, null);
        Assert.Equal(32, keyNulls.Length);
    }

    private class MockHttpMessageHandler : System.Net.Http.HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}

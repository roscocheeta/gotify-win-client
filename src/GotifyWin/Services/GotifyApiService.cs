using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using GotifyWin.Models;

namespace GotifyWin.Services;

public class GotifyApiService : IGotifyApiService
{
    private const int MaxIconSizeBytes = 2 * 1024 * 1024; // 2 MB limit
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;

    public GotifyApiService(HttpClient? httpClient = null, string? cacheDirectory = null)
    {
        // SEC: Never follow redirects automatically; .NET forwards custom headers (X-Gotify-Key) across hosts,
        // which would leak the client token to a redirect target.
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 8 * 1024 * 1024 // cap buffered JSON/API responses
        };
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GotifyWin", "cache", "icons"
        );
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Gotify-Key", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static string NormalizeUrl(string serverUrl) => serverUrl.Trim().TrimEnd('/');

    public async Task<bool> TestConnectionAsync(string serverUrl, string clientToken, CancellationToken ct = default)
    {
        try
        {
            var user = await GetCurrentUserAsync(serverUrl, clientToken, ct);
            return user != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<GotifyUser?> GetCurrentUserAsync(string serverUrl, string clientToken, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(serverUrl)}/current/user";
        using var request = CreateRequest(HttpMethod.Get, url, clientToken);
        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<GotifyUser>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task<List<GotifyApplication>> GetApplicationsAsync(string serverUrl, string clientToken, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(serverUrl)}/application";
        using var request = CreateRequest(HttpMethod.Get, url, clientToken);
        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return new List<GotifyApplication>();

        var json = await response.Content.ReadAsStringAsync(ct);
        var apps = JsonSerializer.Deserialize<List<GotifyApplication>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new List<GotifyApplication>();

        // Populate cached local icon paths safely
        foreach (var app in apps)
        {
            // Sanitize App ID to avoid directory traversal
            var safeAppId = Math.Abs(app.Id);
            var cachedPath = Path.Combine(_cacheDirectory, $"app_{safeAppId}.png");
            if (File.Exists(cachedPath))
            {
                app.LocalIconPath = cachedPath;
            }
        }

        return apps;
    }

    public async Task<string?> CacheApplicationIconAsync(string serverUrl, string clientToken, GotifyApplication app, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(app.Image))
            return null;

        try
        {
            if (!Directory.Exists(_cacheDirectory))
                Directory.CreateDirectory(_cacheDirectory);

            var safeAppId = Math.Abs(app.Id);
            var localPath = Path.Combine(_cacheDirectory, $"app_{safeAppId}.png");
            if (File.Exists(localPath))
            {
                app.LocalIconPath = localPath;
                return localPath;
            }

            var normalizedServer = NormalizeUrl(serverUrl);
            var serverUri = new Uri(normalizedServer);

            string targetUrl;

            if (Uri.TryCreate(app.Image, UriKind.Absolute, out var parsedUri))
            {
                // SEC: Only fetch icons from the configured Gotify server origin (prevents SSRF/IP leaks to third parties)
                if (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps)
                    return null;
                if (!ImageSafety.IsSameOrigin(parsedUri, serverUri))
                    return null;

                targetUrl = parsedUri.ToString();
            }
            else
            {
                // Relative URL -> strictly same origin
                targetUrl = $"{normalizedServer}/{app.Image.TrimStart('/')}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            // SEC-01: Token is only ever sent to the configured Gotify server origin (enforced above)
            request.Headers.Add("X-Gotify-Key", clientToken);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            // SEC-09: Validate content type and content length
            if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > MaxIconSizeBytes)
                return null;

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType != null && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                if (ms.Length + bytesRead > MaxIconSizeBytes)
                {
                    // Truncate/abort if exceeds 2MB
                    return null;
                }
                ms.Write(buffer, 0, bytesRead);
            }

            var iconBytes = ms.ToArray();
            // SEC: Verify the payload is really a supported raster image before persisting it
            if (!ImageSafety.HasKnownImageSignature(iconBytes))
                return null;

            await File.WriteAllBytesAsync(localPath, iconBytes, ct);
            app.LocalIconPath = localPath;
            return localPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GotifyApiService] Error caching icon: {ex.Message}");
        }

        return null;
    }

    public async Task<List<GotifyMessage>> GetRecentMessagesAsync(string serverUrl, string clientToken, int limit = 50, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(serverUrl)}/message?limit={limit}";
        using var request = CreateRequest(HttpMethod.Get, url, clientToken);
        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            return new List<GotifyMessage>();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("messages", out var messagesElement) && messagesElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<GotifyMessage>>(messagesElement.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new List<GotifyMessage>();
        }

        return new List<GotifyMessage>();
    }

    public async Task<bool> DeleteMessageAsync(string serverUrl, string clientToken, long messageId, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(serverUrl)}/message/{messageId}";
        using var request = CreateRequest(HttpMethod.Delete, url, clientToken);
        using var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ClearAllMessagesAsync(string serverUrl, string clientToken, CancellationToken ct = default)
    {
        var url = $"{NormalizeUrl(serverUrl)}/message";
        using var request = CreateRequest(HttpMethod.Delete, url, clientToken);
        using var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }
}

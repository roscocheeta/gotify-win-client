using System.IO;
using System.Net.Http;
using System.Text;
using GotifyWin.Models;

namespace GotifyWin.Services;

public interface IActionExecutionService
{
    Task<ActionExecutionResult> ExecuteWebhookAsync(GotifyMessageAction action, CancellationToken ct = default);
}

public record ActionExecutionResult(bool Success, string Message);

public class ActionExecutionService : IActionExecutionService
{
    private const int MaxBodyBytes = 64 * 1024; // 64 KB cap

    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE"
    };

    // Dedicated HttpClient without Gotify client tokens so webhooks never leak credentials
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5399:HttpClients should check for certificate revocation list", Justification = "Internal homelab webhooks commonly use private-CA certificates without CRL/OCSP endpoints; OS X.509 chain trust remains enforced.")]
    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly HttpClient _httpClient;

    public ActionExecutionService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<ActionExecutionResult> ExecuteWebhookAsync(GotifyMessageAction action, CancellationToken ct = default)
    {
        if (action == null || action.Type != GotifyActionType.Http)
        {
            return new ActionExecutionResult(false, "Invalid action type.");
        }

        // SEC: Strictly allow only HTTP/HTTPS schemes
        if (!Uri.TryCreate(action.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new ActionExecutionResult(false, "Rejected unsafe or invalid URL scheme.");
        }

        var methodStr = string.IsNullOrWhiteSpace(action.Method) ? "POST" : action.Method.Trim().ToUpperInvariant();
        if (!AllowedMethods.Contains(methodStr))
        {
            return new ActionExecutionResult(false, $"Unsupported HTTP method '{methodStr}'.");
        }

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(methodStr), uri);

            if (action.Headers != null)
            {
                foreach (var (key, val) in action.Headers)
                {
                    if (string.IsNullOrWhiteSpace(key) ||
                        key.Contains('\r') || key.Contains('\n') ||
                        val.Contains('\r') || val.Contains('\n'))
                    {
                        continue;
                    }

                    if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    request.Headers.TryAddWithoutValidation(key.Trim(), val);
                }
            }

            if (!string.IsNullOrEmpty(action.Body) && methodStr != "GET")
            {
                var bodyBytes = Encoding.UTF8.GetByteCount(action.Body);
                if (bodyBytes > MaxBodyBytes)
                {
                    return new ActionExecutionResult(false, "Webhook body exceeds 64 KB limit.");
                }

                var contentType = "application/json";
                if (action.Headers != null &&
                    action.Headers.TryGetValue("Content-Type", out var customCt) &&
                    !string.IsNullOrWhiteSpace(customCt) &&
                    !customCt.Contains('\r') && !customCt.Contains('\n'))
                {
                    contentType = customCt.Split(';')[0].Trim();
                }

                request.Content = new StringContent(action.Body, Encoding.UTF8, contentType);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode)
            {
                return new ActionExecutionResult(true, $"HTTP {(int)response.StatusCode}");
            }

            return new ActionExecutionResult(false, $"HTTP {(int)response.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActionExecutionService] Webhook request failed: {ex.Message}");
            return new ActionExecutionResult(false, "Network error");
        }
        catch (TaskCanceledException)
        {
            return new ActionExecutionResult(false, "Timed out");
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActionExecutionService] Webhook I/O error: {ex.Message}");
            return new ActionExecutionResult(false, "I/O error");
        }
    }
}

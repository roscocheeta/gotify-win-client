using GotifyWin.Models;

namespace GotifyWin.Services;

public interface IGotifyApiService
{
    Task<bool> TestConnectionAsync(string serverUrl, string clientToken, CancellationToken ct = default);
    Task<GotifyUser?> GetCurrentUserAsync(string serverUrl, string clientToken, CancellationToken ct = default);
    Task<List<GotifyApplication>> GetApplicationsAsync(string serverUrl, string clientToken, CancellationToken ct = default);
    Task<string?> CacheApplicationIconAsync(string serverUrl, string clientToken, GotifyApplication app, CancellationToken ct = default);
    Task<List<GotifyMessage>> GetRecentMessagesAsync(string serverUrl, string clientToken, int limit = 50, CancellationToken ct = default);
    Task<bool> DeleteMessageAsync(string serverUrl, string clientToken, long messageId, CancellationToken ct = default);
    Task<bool> ClearAllMessagesAsync(string serverUrl, string clientToken, CancellationToken ct = default);
}

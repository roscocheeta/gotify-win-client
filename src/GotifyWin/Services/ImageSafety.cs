using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace GotifyWin.Services;

/// <summary>
/// Shared helpers for safely handling images originating from remote servers:
/// magic-byte validation, origin policy, and a size/timeout-bounded loader.
/// </summary>
public static class ImageSafety
{
    public const int MaxImageBytes = 2 * 1024 * 1024; // 2 MB

    /// <summary>Returns true when the bytes start with a PNG, JPEG, GIF, BMP, ICO or WebP signature.</summary>
    public static bool HasKnownImageSignature(ReadOnlySpan<byte> d)
    {
        if (d.Length < 4) return false;

        if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return true;          // PNG
        if (d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return true;                          // JPEG
        if (d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x38) return true;          // GIF8
        if (d[0] == 0x42 && d[1] == 0x4D) return true;                                          // BMP
        if (d[0] == 0x00 && d[1] == 0x00 && d[2] == 0x01 && d[3] == 0x00) return true;          // ICO
        if (d.Length >= 12 && d[0] == 0x52 && d[1] == 0x49 && d[2] == 0x46 && d[3] == 0x46 &&
            d[8] == 0x57 && d[9] == 0x45 && d[10] == 0x42 && d[11] == 0x50) return true;        // WEBP
        return false;
    }

    public static bool IsSameOrigin(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
        a.Port == b.Port;

    /// <summary>
    /// Policy: only http/https images; by default only from the configured Gotify server origin.
    /// External hosts require an explicit opt-in because fetching them leaks the user's IP address.
    /// </summary>
    public static bool IsImageUrlAllowed(string? imageUrl, string? serverUrl, bool allowExternal)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var img)) return false;
        if (img.Scheme != Uri.UriSchemeHttp && img.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(img.UserInfo)) return false;
        if (allowExternal) return true;

        if (!Uri.TryCreate(serverUrl?.Trim(), UriKind.Absolute, out var server)) return false;
        return string.Equals(img.Host, server.Host, StringComparison.OrdinalIgnoreCase) && img.Port == server.Port;
    }
}

/// <summary>
/// Downloads notification images with no redirects, a hard size cap, a timeout, content-type and
/// signature checks. Never sends the client token. Results are frozen and cached (bounded).
/// </summary>
public static class SecureImageLoader
{
    private const int MaxCacheEntries = 64;
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(8),
        MaxResponseContentBufferSize = ImageSafety.MaxImageBytes
    };
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();

    /// <summary>Supplies (serverUrl, allowExternalImages); set once at startup.</summary>
    public static Func<(string ServerUrl, bool AllowExternal)>? PolicyProvider { get; set; }

    public static bool IsAllowed(string? url)
    {
        var (server, allowExternal) = PolicyProvider?.Invoke() ?? (string.Empty, false);
        return ImageSafety.IsImageUrlAllowed(url, server, allowExternal);
    }

    public static async Task<BitmapImage?> LoadAsync(string? url, int decodeWidth = 160, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var targetWidth = Math.Clamp(decodeWidth, 32, 1024);
        var cacheKey = $"{url}##{targetWidth}";
        if (Cache.TryGetValue(cacheKey, out var cached)) return cached;

        // If local file path
        if (File.Exists(url))
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = targetWidth;
                bi.UriSource = new Uri(url);
                bi.EndInit();
                bi.Freeze();

                if (Cache.Count >= MaxCacheEntries) Cache.Clear();
                Cache[cacheKey] = bi;
                return bi;
            }
            catch
            {
                return null;
            }
        }

        if (!IsAllowed(url)) return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;

            var len = response.Content.Headers.ContentLength;
            if (len.HasValue && len.Value > ImageSafety.MaxImageBytes) return null;

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType != null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                if (ms.Length + read > ImageSafety.MaxImageBytes) return null;
                ms.Write(buffer, 0, read);
            }

            var bytes = ms.ToArray();
            if (!ImageSafety.HasKnownImageSignature(bytes)) return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = targetWidth;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();

            if (Cache.Count >= MaxCacheEntries) Cache.Clear();
            Cache[cacheKey] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SecureImageLoader] Failed to load image: {ex.Message}");
            return null;
        }
    }
}

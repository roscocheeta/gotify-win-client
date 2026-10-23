using System.Net;

namespace GotifyWin.Services;

public record ServerUrlValidation(bool IsValid, string? Error, string? Warning, Uri? Uri);

/// <summary>
/// Validates the configured Gotify server URL and flags insecure transport (plain HTTP)
/// to non-local hosts, where the client token would travel unencrypted.
/// </summary>
public static class ServerUrlValidator
{
    public static ServerUrlValidation Validate(string? serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            return new(false, "Server URL is required.", null, null);

        var trimmed = serverUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new(false, "Server URL must start with http:// or https://.", null, null);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return new(false, "Server URL must not contain embedded credentials.", null, null);

        string? warning = null;
        if (uri.Scheme == Uri.UriSchemeHttp && !IsLocalOrPrivateHost(uri.Host))
        {
            warning = "Connection is not encrypted (http://). Your client token can be intercepted. Use https:// when possible.";
        }

        return new(true, null, warning, uri);
    }

    public static bool IsLocalOrPrivateHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;

            var b = ip.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        }

        // Single-label hostname (e.g. "nas") is a LAN name
        return !host.Contains('.');
    }
}

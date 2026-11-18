namespace GotifyWin.Models;

public enum GotifyActionType
{
    View,
    Http
}

public class GotifyMessageAction
{
    public GotifyActionType Type { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Method { get; init; } = "POST";
    public Dictionary<string, string>? Headers { get; init; }
    public string? Body { get; init; }
    public bool Clear { get; init; }

    public string DisplayIcon => Type == GotifyActionType.Http ? "⚡" : "🔗";
    public string DisplayLabel => $"{DisplayIcon} {Label}";

    public string ActionTooltip
    {
        get
        {
            if (Uri.TryCreate(Url, UriKind.Absolute, out var uri))
            {
                return Type == GotifyActionType.Http
                    ? $"Webhook {Method} -> {uri.Host}"
                    : $"Open {uri.Host}";
            }
            return Url;
        }
    }
}

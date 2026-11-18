using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GotifyWin.Models;

public class GotifyMessage : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("appid")]
    public long AppId { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 5;

    private DateTime _date = DateTime.UtcNow;

    [JsonPropertyName("date")]
    public DateTime Date
    {
        get => _date;
        set
        {
            var normalized = value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value;
            _date = normalized;
            OnPropertyChanged(nameof(Date));
            OnPropertyChanged(nameof(FormattedDate));
            OnPropertyChanged(nameof(RelativeTime));
        }
    }

    [JsonPropertyName("extras")]
    public Dictionary<string, JsonElement>? Extras { get; set; }

    // Enriched Presentation Properties
    [JsonIgnore]
    public string ApplicationName { get; set; } = string.Empty;

    [JsonIgnore]
    public string? ApplicationIconPath { get; set; }

    private int _duplicateCount = 1;

    [JsonIgnore]
    public int DuplicateCount
    {
        get => _duplicateCount;
        set
        {
            if (_duplicateCount != value)
            {
                _duplicateCount = value;
                OnPropertyChanged(nameof(DuplicateCount));
            }
        }
    }

    [JsonIgnore]
    public string? FilteredByRuleName { get; set; }

    [JsonIgnore]
    public bool IsFiltered => !string.IsNullOrEmpty(FilteredByRuleName);

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? (string.IsNullOrWhiteSpace(ApplicationName) ? "Gotify Message" : ApplicationName) : Title;

    [JsonIgnore]
    public string DisplayAppName => string.IsNullOrWhiteSpace(ApplicationName) ? $"App #{AppId}" : ApplicationName;

    [JsonIgnore]
    public string? ClickUrl
    {
        get
        {
            if (Extras != null && Extras.TryGetValue("client::notification", out var clientNotif))
            {
                if (clientNotif.ValueKind == JsonValueKind.Object &&
                    clientNotif.TryGetProperty("click", out var clickProp) &&
                    clickProp.TryGetProperty("url", out var urlProp) &&
                    urlProp.ValueKind == JsonValueKind.String)
                {
                    return urlProp.GetString();
                }
            }
            return null;
        }
    }

    [JsonIgnore]
    public bool HasClickUrl => !string.IsNullOrWhiteSpace(ClickUrl);

    [JsonIgnore]
    public string? ClickUrlHost
    {
        get
        {
            if (HasClickUrl &&
                Uri.TryCreate(ClickUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return uri.Host;
            }
            return null;
        }
    }

    [JsonIgnore]
    public string ClickUrlButtonText => string.IsNullOrWhiteSpace(ClickUrlHost)
        ? "🔗 Open Link"
        : $"🔗 Open {ClickUrlHost}";

    [JsonIgnore]
    public List<GotifyMessageAction> MessageActions
    {
        get
        {
            var result = new List<GotifyMessageAction>();
            if (Extras == null) return result;

            JsonElement actionsElement = default;
            bool found = false;

            if (Extras.TryGetValue("client::notification", out var clientNotif) &&
                clientNotif.ValueKind == JsonValueKind.Object &&
                clientNotif.TryGetProperty("actions", out var nestedActions) &&
                nestedActions.ValueKind == JsonValueKind.Array)
            {
                actionsElement = nestedActions;
                found = true;
            }
            else if (Extras.TryGetValue("actions", out var topActions) &&
                     topActions.ValueKind == JsonValueKind.Array)
            {
                actionsElement = topActions;
                found = true;
            }

            if (!found) return result;

            foreach (var item in actionsElement.EnumerateArray())
            {
                if (result.Count >= 5) break;
                if (item.ValueKind != JsonValueKind.Object) continue;

                var parsed = ParseActionElement(item);
                if (parsed != null)
                {
                    result.Add(parsed);
                }
            }

            return result;
        }
    }

    [JsonIgnore]
    public bool HasMessageActions => MessageActions.Count > 0;

    private static GotifyMessageAction? ParseActionElement(JsonElement item)
    {
        string? rawType = GetFirstStringProp(item, "action", "type");
        GotifyActionType actionType;
        if (string.Equals(rawType, "http", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawType, "webhook", StringComparison.OrdinalIgnoreCase))
        {
            actionType = GotifyActionType.Http;
        }
        else if (string.IsNullOrWhiteSpace(rawType) ||
                 string.Equals(rawType, "view", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(rawType, "url", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(rawType, "open", StringComparison.OrdinalIgnoreCase))
        {
            actionType = GotifyActionType.View;
        }
        else
        {
            return null;
        }

        string? label = GetFirstStringProp(item, "label", "title", "text");
        string? url = GetFirstStringProp(item, "url");

        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(url))
            return null;

        // SEC: Strictly enforce HTTP/HTTPS scheme on all interactive action URLs
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var cleanLabel = label.Trim();
        if (cleanLabel.Length > 40)
        {
            cleanLabel = cleanLabel[..37] + "...";
        }

        string method = "POST";
        if (actionType == GotifyActionType.Http)
        {
            var rawMethod = GetFirstStringProp(item, "method");
            if (!string.IsNullOrWhiteSpace(rawMethod))
            {
                var upper = rawMethod.Trim().ToUpperInvariant();
                if (upper is "GET" or "POST" or "PUT" or "PATCH" or "DELETE")
                {
                    method = upper;
                }
                else
                {
                    return null;
                }
            }
        }

        Dictionary<string, string>? headers = null;
        if (item.TryGetProperty("headers", out var headersProp) && headersProp.ValueKind == JsonValueKind.Object)
        {
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in headersProp.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var val = prop.Value.GetString() ?? string.Empty;
                    if (!prop.Name.Contains('\r') && !prop.Name.Contains('\n') &&
                        !val.Contains('\r') && !val.Contains('\n'))
                    {
                        headers[prop.Name] = val;
                    }
                }
            }
        }

        string? body = null;
        if (item.TryGetProperty("body", out var bodyProp))
        {
            body = bodyProp.ValueKind switch
            {
                JsonValueKind.String => bodyProp.GetString(),
                JsonValueKind.Object or JsonValueKind.Array => bodyProp.GetRawText(),
                _ => null
            };
        }

        bool clear = item.TryGetProperty("clear", out var clearProp) &&
                     clearProp.ValueKind == JsonValueKind.True;

        return new GotifyMessageAction
        {
            Type = actionType,
            Label = cleanLabel,
            Url = url,
            Method = method,
            Headers = headers,
            Body = body,
            Clear = clear
        };
    }

    private static string? GetFirstStringProp(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        return null;
    }

    [JsonIgnore]
    public string? ImageUrl
    {
        get
        {
            // 1. Check Gotify Android/Web client notification extras
            if (Extras != null && Extras.TryGetValue("client::notification", out var clientNotif))
            {
                if (clientNotif.ValueKind == JsonValueKind.Object)
                {
                    if (clientNotif.TryGetProperty("bigImageUrl", out var bigProp) &&
                        bigProp.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(bigProp.GetString()))
                    {
                        return bigProp.GetString();
                    }

                    if (clientNotif.TryGetProperty("image", out var imgProp) &&
                        imgProp.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(imgProp.GetString()))
                    {
                        return imgProp.GetString();
                    }
                }
            }

            // 2. Check for markdown image link: ![...](https://...)
            if (!string.IsNullOrWhiteSpace(Message))
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    Message,
                    @"!\[.*?\]\((https?://[^\s\)]+)\)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }

                // 3. Check for standalone image URL ending with common image extension
                var directMatch = System.Text.RegularExpressions.Regex.Match(
                    Message,
                    @"(https?://\S+\.(?:png|jpe?g|gif|webp|bmp))(?:\?\S*)?",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                if (directMatch.Success)
                {
                    return directMatch.Groups[1].Value;
                }
            }

            return null;
        }
    }

    [JsonIgnore]
    public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);

    [JsonIgnore]
    public string PriorityLabel => Priority switch
    {
        >= 9 => "Urgent",
        >= 7 => "High",
        >= 4 => "Normal",
        >= 1 => "Low",
        _ => "Silent"
    };

    [JsonIgnore]
    public string PriorityColor => Priority switch
    {
        >= 9 => "#DC2626", // Crimson Red
        >= 7 => "#D97706", // Deep Amber (passes WCAG AA 4.5:1 with white text)
        >= 4 => "#0284C7", // Sky / Cerulean Blue
        >= 1 => "#64748B", // Slate
        _ => "#475569"     // Muted Slate
    };

    [JsonIgnore]
    public string PriorityTextColor => "#FFFFFF";

    [JsonIgnore]
    public string PriorityDisplay => $"{PriorityLabel} ({Priority})";

    [JsonIgnore]
    public string PriorityTooltip => $"Priority {Priority}/10: {PriorityLabel} priority notification";

    [JsonIgnore]
    public string FormattedDate => Date.ToLocalTime().ToString("MMM dd, yyyy HH:mm:ss");

    [JsonIgnore]
    public string RelativeTime
    {
        get
        {
            var utcDate = Date.Kind == DateTimeKind.Utc ? Date : Date.ToUniversalTime();
            var span = DateTime.UtcNow - utcDate;
            if (span.TotalSeconds < 45) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return Date.ToLocalTime().ToString("MMM dd");
        }
    }

    public void RefreshRelativeTime()
    {
        OnPropertyChanged(nameof(RelativeTime));
    }
}

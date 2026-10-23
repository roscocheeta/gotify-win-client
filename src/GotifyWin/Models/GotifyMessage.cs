using System.Text.Json;
using System.Text.Json.Serialization;

namespace GotifyWin.Models;

public class GotifyMessage
{
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

    [JsonPropertyName("date")]
    public DateTime Date { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("extras")]
    public Dictionary<string, JsonElement>? Extras { get; set; }

    // Enriched Presentation Properties
    [JsonIgnore]
    public string ApplicationName { get; set; } = string.Empty;

    [JsonIgnore]
    public string? ApplicationIconPath { get; set; }

    [JsonIgnore]
    public int DuplicateCount { get; set; } = 1;

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
            var span = DateTime.UtcNow - (Date.Kind == DateTimeKind.Utc ? Date : Date.ToUniversalTime());
            if (span.TotalSeconds < 45) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return Date.ToLocalTime().ToString("MMM dd");
        }
    }
}

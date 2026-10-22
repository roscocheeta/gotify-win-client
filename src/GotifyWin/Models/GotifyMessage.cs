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
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? (string.IsNullOrWhiteSpace(ApplicationName) ? "Gotify Alert" : ApplicationName) : Title;

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
    public string PriorityLabel => Priority switch
    {
        >= 8 => "Urgent",
        >= 5 => "High",
        >= 3 => "Normal",
        _ => "Low"
    };

    [JsonIgnore]
    public string PriorityColor => Priority switch
    {
        >= 8 => "#EF4444", // Red
        >= 5 => "#F59E0B", // Amber
        >= 3 => "#3B82F6", // Blue
        _ => "#6B7280"     // Muted Gray
    };

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

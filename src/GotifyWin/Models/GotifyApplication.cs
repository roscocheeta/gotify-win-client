using System.Text.Json.Serialization;

namespace GotifyWin.Models;

public class GotifyApplication
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("image")]
    public string Image { get; set; } = string.Empty;

    [JsonPropertyName("defaultPriority")]
    public int DefaultPriority { get; set; } = 5;

    [JsonIgnore]
    public string? LocalIconPath { get; set; }
}

public class GotifyUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("admin")]
    public bool Admin { get; set; }
}

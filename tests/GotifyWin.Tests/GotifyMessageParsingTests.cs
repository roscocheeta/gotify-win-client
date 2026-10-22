using System.Text.Json;
using GotifyWin.Models;
using Xunit;

namespace GotifyWin.Tests;

public class GotifyMessageParsingTests
{
    [Fact]
    public void Deserialize_StandardGotifyMessage_ParsesAllFields()
    {
        var json = @"{
            ""id"": 105,
            ""appid"": 3,
            ""message"": ""Container nginx restarted unexpectedly"",
            ""title"": ""Docker Alert"",
            ""priority"": 7,
            ""date"": ""2026-10-22T04:15:30Z"",
            ""extras"": {
                ""client::notification"": {
                    ""click"": {
                        ""url"": ""https://portainer.homelab.lan/containers""
                    }
                },
                ""client::display"": {
                    ""contentType"": ""text/markdown""
                }
            }
        }";

        var msg = JsonSerializer.Deserialize<GotifyMessage>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(msg);
        Assert.Equal(105, msg.Id);
        Assert.Equal(3, msg.AppId);
        Assert.Equal("Docker Alert", msg.Title);
        Assert.Equal("Container nginx restarted unexpectedly", msg.Message);
        Assert.Equal(7, msg.Priority);
        Assert.Equal("https://portainer.homelab.lan/containers", msg.ClickUrl);
        Assert.True(msg.HasClickUrl);
        Assert.Equal("High", msg.PriorityLabel);
        Assert.Equal("#F59E0B", msg.PriorityColor);
    }

    [Fact]
    public void RelativeTime_CalculatesAppropriateStrings()
    {
        var justNow = new GotifyMessage { Date = DateTime.UtcNow.AddSeconds(-15) };
        Assert.Equal("Just now", justNow.RelativeTime);

        var fiveMinsAgo = new GotifyMessage { Date = DateTime.UtcNow.AddMinutes(-5) };
        Assert.Equal("5m ago", fiveMinsAgo.RelativeTime);

        var twoHoursAgo = new GotifyMessage { Date = DateTime.UtcNow.AddHours(-2) };
        Assert.Equal("2h ago", twoHoursAgo.RelativeTime);

        var threeDaysAgo = new GotifyMessage { Date = DateTime.UtcNow.AddDays(-3) };
        Assert.Equal("3d ago", threeDaysAgo.RelativeTime);
    }

    [Fact]
    public void PriorityMapping_CalculatesCorrectColorsAndLabels()
    {
        var urgent = new GotifyMessage { Priority = 9 };
        Assert.Equal("Urgent", urgent.PriorityLabel);
        Assert.Equal("#EF4444", urgent.PriorityColor);

        var high = new GotifyMessage { Priority = 6 };
        Assert.Equal("High", high.PriorityLabel);
        Assert.Equal("#F59E0B", high.PriorityColor);

        var normal = new GotifyMessage { Priority = 4 };
        Assert.Equal("Normal", normal.PriorityLabel);
        Assert.Equal("#3B82F6", normal.PriorityColor);

        var low = new GotifyMessage { Priority = 1 };
        Assert.Equal("Low", low.PriorityLabel);
        Assert.Equal("#6B7280", low.PriorityColor);
    }
}

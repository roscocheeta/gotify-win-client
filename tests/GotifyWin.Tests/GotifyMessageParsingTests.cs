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
        Assert.Equal("#D97706", msg.PriorityColor);
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
        Assert.Equal("#DC2626", urgent.PriorityColor);

        var high = new GotifyMessage { Priority = 7 };
        Assert.Equal("High", high.PriorityLabel);
        Assert.Equal("#D97706", high.PriorityColor);

        // Gotify server default priority 5 is now mapped to Normal (relieving alert fatigue)
        var normal = new GotifyMessage { Priority = 5 };
        Assert.Equal("Normal", normal.PriorityLabel);
        Assert.Equal("#0284C7", normal.PriorityColor);

        var low = new GotifyMessage { Priority = 2 };
        Assert.Equal("Low", low.PriorityLabel);
        Assert.Equal("#64748B", low.PriorityColor);

        var silent = new GotifyMessage { Priority = 0 };
        Assert.Equal("Silent", silent.PriorityLabel);
        Assert.Equal("#475569", silent.PriorityColor);
    }

    [Fact]
    public void ImageUrl_FromExtrasBigImageUrl_ExtractsCorrectly()
    {
        var json = @"{
            ""id"": 201,
            ""appid"": 5,
            ""message"": ""Front door motion detected"",
            ""title"": ""Security"",
            ""priority"": 5,
            ""extras"": {
                ""client::notification"": {
                    ""bigImageUrl"": ""https://homeassistant.homelab.lan/api/camera/snapshot.jpg""
                }
            }
        }";

        var msg = JsonSerializer.Deserialize<GotifyMessage>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(msg);
        Assert.True(msg.HasImage);
        Assert.Equal("https://homeassistant.homelab.lan/api/camera/snapshot.jpg", msg.ImageUrl);
    }

    [Fact]
    public void ImageUrl_FromMarkdownSyntax_ExtractsCorrectly()
    {
        var msg = new GotifyMessage
        {
            Id = 202,
            Title = "Snapshot",
            Message = "Motion detected at front gate: ![snapshot](https://frigate.lan/api/events/123/snapshot.jpg)"
        };

        Assert.True(msg.HasImage);
        Assert.Equal("https://frigate.lan/api/events/123/snapshot.jpg", msg.ImageUrl);
    }

    [Fact]
    public void ImageUrl_FromDirectLink_ExtractsCorrectly()
    {
        var msg = new GotifyMessage
        {
            Id = 203,
            Title = "Plot generated",
            Message = "Network utilization plot: https://graphs.lan/daily_bandwidth.png"
        };

        Assert.True(msg.HasImage);
        Assert.Equal("https://graphs.lan/daily_bandwidth.png", msg.ImageUrl);
    }
}

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GotifyWin.Models;
using GotifyWin.Services;
using Xunit;

namespace GotifyWin.Tests;

public class GotifyApiServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task TestConnectionAsync_WhenServerReturns200WithUser_ReturnsTrue()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("https://gotify.example.com/current/user", req.RequestUri?.ToString());
            Assert.True(req.Headers.Contains("X-Gotify-Key"));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\": 1, \"name\": \"admin\", \"admin\": true}", Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var service = new GotifyApiService(client);

        var result = await service.TestConnectionAsync("https://gotify.example.com", "valid-token");
        Assert.True(result);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenServerReturns401Unauthorized_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\": \"Unauthorized\"}", Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var service = new GotifyApiService(client);

        var result = await service.TestConnectionAsync("https://gotify.example.com", "invalid-token");
        Assert.False(result);
    }

    [Fact]
    public async Task GetApplicationsAsync_ParsesApplicationsSuccessfully()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("https://gotify.example.com/application", req.RequestUri?.ToString());
            var json = "[{\"id\": 5, \"name\": \"Sonarr\", \"description\": \"TV Show Downloader\", \"image\": \"image/sonarr.png\"}]";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var service = new GotifyApiService(client);

        var apps = await service.GetApplicationsAsync("https://gotify.example.com", "token");
        Assert.Single(apps);
        Assert.Equal(5, apps[0].Id);
        Assert.Equal("Sonarr", apps[0].Name);
    }

    [Fact]
    public async Task GetRecentMessagesAsync_ParsesMessagesPayload()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("https://gotify.example.com/message?limit=10", req.RequestUri?.ToString());
            var json = "{\"messages\": [{\"id\": 101, \"appid\": 5, \"title\": \"Download Done\", \"message\": \"Series S01E01\", \"priority\": 5}]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        using var client = new HttpClient(handler);
        var service = new GotifyApiService(client);

        var messages = await service.GetRecentMessagesAsync("https://gotify.example.com", "token", limit: 10);
        Assert.Single(messages);
        Assert.Equal(101, messages[0].Id);
        Assert.Equal("Download Done", messages[0].Title);
        Assert.Equal(5, messages[0].Priority);
    }

    [Fact]
    public async Task DeleteMessageAsync_WhenServerReturns200_ReturnsTrue()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal("https://gotify.example.com/message/101", req.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler);
        var service = new GotifyApiService(client);

        var deleted = await service.DeleteMessageAsync("https://gotify.example.com", "token", 101);
        Assert.True(deleted);
    }

    [Fact]
    public async Task ClearAllMessagesAsync_WhenServerReturns200_ReturnsTrue()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal("https://gotify.example.com/message", req.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler);
        var service = new GotifyApiService(client);

        var cleared = await service.ClearAllMessagesAsync("https://gotify.example.com", "token");
        Assert.True(cleared);
    }
}

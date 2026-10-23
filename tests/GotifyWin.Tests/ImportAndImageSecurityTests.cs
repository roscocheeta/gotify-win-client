using System.IO;
using System.Net;
using System.Net.Http;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using Xunit;

namespace GotifyWin.Tests;

public class ImportAndImageSecurityTests
{
    // ---------- Image policy / signatures ----------

    [Theory]
    [InlineData("https://gotify.example.com/image/a.png", "https://gotify.example.com", false, true)]
    [InlineData("https://evil.example.net/pixel.png", "https://gotify.example.com", false, false)]
    [InlineData("https://evil.example.net/pixel.png", "https://gotify.example.com", true, true)]
    [InlineData("https://gotify.example.com:8443/a.png", "https://gotify.example.com", false, false)]
    [InlineData("file:///C:/secret.png", "https://gotify.example.com", true, false)]
    [InlineData("ftp://gotify.example.com/a.png", "https://gotify.example.com", true, false)]
    [InlineData("https://user:pw@gotify.example.com/a.png", "https://gotify.example.com", true, false)]
    [InlineData("not a url", "https://gotify.example.com", true, false)]
    public void ImageSafety_UrlPolicy(string image, string server, bool allowExternal, bool expected)
    {
        Assert.Equal(expected, ImageSafety.IsImageUrlAllowed(image, server, allowExternal));
    }

    [Fact]
    public void ImageSafety_Signature_AcceptsRealImages_RejectsOthers()
    {
        Assert.True(ImageSafety.HasKnownImageSignature(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
        Assert.True(ImageSafety.HasKnownImageSignature(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        Assert.True(ImageSafety.HasKnownImageSignature(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }));
        Assert.False(ImageSafety.HasKnownImageSignature("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8));
        Assert.False(ImageSafety.HasKnownImageSignature("MZ\x90\x00 executable"u8));
        Assert.False(ImageSafety.HasKnownImageSignature(new byte[] { 1, 2 }));
    }

    [Fact]
    public async Task GotifyApiService_IconWithNonImageBytes_IsNotCached()
    {
        var handler = new StubHandler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("<html>not an image</html>"u8.ToArray()) };
            r.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png"); // lying header
            return r;
        });
        using var client = new HttpClient(handler);
        var cache = Path.Combine(Path.GetTempPath(), $"gotify_cache_{Guid.NewGuid():N}");
        var service = new GotifyApiService(client, cache);

        try
        {
            var app = new GotifyApplication { Id = 5, Name = "X", Image = "image/x.png" };
            var result = await service.CacheApplicationIconAsync("https://gotify.example.com", "tok", app);

            Assert.Null(result);
            Assert.False(File.Exists(Path.Combine(cache, "app_5.png")));
        }
        finally
        {
            if (Directory.Exists(cache)) try { Directory.Delete(cache, true); } catch { }
        }
    }

    // ---------- Import validation ----------

    [Fact]
    public void ImportValidator_SanitizeRules_DropsInvalidAndCaps()
    {
        var rules = new List<NotificationRule>
        {
            new() { Name = "ok", Pattern = "abc", Operator = RuleOperator.Contains },
            new() { Name = "empty", Pattern = "" },
            new() { Name = "toolong", Pattern = new string('a', ImportValidator.MaxPatternLength + 1) },
            new() { Name = "badregex", Pattern = "([unclosed", Operator = RuleOperator.RegexMatch },
            new() { Name = "badenum", Pattern = "x", Operator = (RuleOperator)999 },
            new() { Name = new string('n', 500), Pattern = "longname" },
        };

        var result = ImportValidator.SanitizeRules(rules, out var skipped);

        Assert.Equal(2, result.Count);
        Assert.Equal(4, skipped);
        Assert.Equal(ImportValidator.MaxNameLength, result[1].Name.Length);
    }

    [Fact]
    public void ImportValidator_SanitizeRules_CapsTotalCount()
    {
        var many = Enumerable.Range(0, ImportValidator.MaxRules + 50)
            .Select(i => new NotificationRule { Name = $"r{i}", Pattern = $"p{i}" })
            .ToList();

        var result = ImportValidator.SanitizeRules(many, out var skipped);

        Assert.Equal(ImportValidator.MaxRules, result.Count);
        Assert.Equal(50, skipped);
    }

    [Fact]
    public void ImportValidator_ClampConfig_BringsValuesIntoRange()
    {
        var c = new GotifyClientConfig { GlobalMinPriority = 99, MaxHistoryCount = int.MaxValue };
        c.Presentation.Width = 1e9;
        c.Presentation.DisplayDurationSeconds = double.NaN;
        c.Presentation.DebounceWindowSeconds = -5;
        c.Presentation.MaxBodyLines = 5000;
        c.Dnd.EmergencyBypassPriority = 0;
        c.Dnd.QuietHoursStart = TimeSpan.FromHours(99);

        ImportValidator.ClampConfig(c);

        Assert.Equal(10, c.GlobalMinPriority);
        Assert.Equal(2000, c.MaxHistoryCount);
        Assert.Equal(600, c.Presentation.Width);
        Assert.Equal(5, c.Presentation.DisplayDurationSeconds);
        Assert.Equal(3, c.Presentation.DebounceWindowSeconds);
        Assert.Equal(10, c.Presentation.MaxBodyLines);
        Assert.Equal(1, c.Dnd.EmergencyBypassPriority);
        Assert.Equal(TimeSpan.Zero, c.Dnd.QuietHoursStart);
    }

    [Fact]
    public void ImportValidator_SanitizeAppFilters_ClampsAndStripsSoundPaths()
    {
        var input = new Dictionary<long, AppFilterSetting>
        {
            [7] = new() { AppId = 99, AppName = "A", MinPriority = 50, CustomSoundPath = @"\\evil\share\x.wav" }
        };

        var result = ImportValidator.SanitizeAppFilters(input);

        Assert.Equal(7, result[7].AppId);
        Assert.Equal(10, result[7].MinPriority);
        Assert.Null(result[7].CustomSoundPath);
    }

    [Fact]
    public void ImportValidator_EnsureFileSizeAllowed_RejectsHugeFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gotify_big_{Guid.NewGuid():N}.json");
        try
        {
            using (var fs = File.Create(path)) fs.SetLength(ImportValidator.MaxImportFileBytes + 1);
            Assert.Throws<InvalidDataException>(() => ImportValidator.EnsureFileSizeAllowed(path));

            File.WriteAllText(path, "{}");
            ImportValidator.EnsureFileSizeAllowed(path); // no throw
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_fn(request));
    }
}

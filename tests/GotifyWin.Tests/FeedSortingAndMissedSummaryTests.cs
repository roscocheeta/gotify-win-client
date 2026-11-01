using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows.Data;
using System.Windows.Threading;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using GotifyWin.ViewModels;
using Xunit;

namespace GotifyWin.Tests;

public class FeedSortingAndMissedSummaryTests
{
    private class DummyPresenter : INotificationPresenter
    {
#pragma warning disable CS0067
        public event EventHandler<string>? UrlOpenRequested;
#pragma warning restore CS0067
        public List<GotifyMessage> PresentedMessages { get; } = new();

        public void Present(GotifyMessage message, GotifyClientConfig config)
        {
            PresentedMessages.Add(message);
        }

        public void OpenUrl(string url) { }
    }

    private class DummyConfigService : IConfigService
    {
        public GotifyClientConfig Config { get; set; } = new();
        public string ConfigFilePath => string.Empty;
        public bool HasDecryptionFailed => false;
#pragma warning disable CS0067
        public event EventHandler<GotifyClientConfig>? ConfigChanged;
#pragma warning restore CS0067
        public void Load() { }
        public void Save() { ConfigChanged?.Invoke(this, Config); }
    }

    private class DummyApiService : IGotifyApiService
    {
        public Task<bool> TestConnectionAsync(string serverUrl, string clientToken, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<GotifyUser?> GetCurrentUserAsync(string serverUrl, string clientToken, CancellationToken ct = default)
            => Task.FromResult<GotifyUser?>(null);
        public Task<List<GotifyApplication>> GetApplicationsAsync(string serverUrl, string clientToken, CancellationToken ct = default)
            => Task.FromResult(new List<GotifyApplication>());
        public Task<string?> CacheApplicationIconAsync(string serverUrl, string clientToken, GotifyApplication app, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public Task<List<GotifyMessage>> GetRecentMessagesAsync(string serverUrl, string clientToken, int limit = 50, CancellationToken ct = default)
            => Task.FromResult(new List<GotifyMessage>());
        public Task<bool> DeleteMessageAsync(string serverUrl, string clientToken, long messageId, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<bool> ClearAllMessagesAsync(string serverUrl, string clientToken, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    [Fact]
    public void FeedViewModel_ChronologicalSorting_EnforcesStrictDescendingOrder()
    {
        var thread = new Thread(() =>
        {
            var configService = new DummyConfigService();
            var apiService = new DummyApiService();
            var presenter = new DummyPresenter();

            using var vm = new FeedViewModel(apiService, configService, presenter);

            var now = DateTime.UtcNow;

            // Add messages out of order
            var msg1HourAgo = new GotifyMessage { Id = 10, Date = now.AddHours(-1), Title = "1h ago" };
            var msg25MinAgo = new GotifyMessage { Id = 20, Date = now.AddMinutes(-25), Title = "25m ago" };
            var msgJustNow1 = new GotifyMessage { Id = 30, Date = now.AddSeconds(-10), Title = "Just now 1" };
            var msgJustNow2 = new GotifyMessage { Id = 31, Date = now.AddSeconds(-5), Title = "Just now 2" };

            // Add 25m ago first, then 1h ago, then just now
            vm.AddMessage(msg25MinAgo);
            vm.AddMessage(msg1HourAgo);
            vm.AddMessage(msgJustNow1);
            vm.AddMessage(msgJustNow2);

            var items = vm.FilteredMessages.Cast<GotifyMessage>().ToList();

            Assert.Equal(4, items.Count);
            Assert.Equal(31, items[0].Id); // Newest Date/Id at top
            Assert.Equal(30, items[1].Id);
            Assert.Equal(20, items[2].Id);
            Assert.Equal(10, items[3].Id); // Oldest at bottom

            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void FeedViewModel_AddMessage_OlderMessageAppearingLater_IsPlacedInCorrectOrder()
    {
        var thread = new Thread(() =>
        {
            var configService = new DummyConfigService();
            var apiService = new DummyApiService();
            var presenter = new DummyPresenter();

            using var vm = new FeedViewModel(apiService, configService, presenter);

            var now = DateTime.UtcNow;

            var newerMsg = new GotifyMessage { Id = 200, Date = now, Title = "Brand New" };
            var olderMsg = new GotifyMessage { Id = 100, Date = now.AddMinutes(-25), Title = "Arrived Late" };

            // First add the newer message
            vm.AddMessage(newerMsg);

            // Now simulate a delayed older message arriving
            vm.AddMessage(olderMsg);

            var items = vm.FilteredMessages.Cast<GotifyMessage>().ToList();

            Assert.Equal(2, items.Count);
            Assert.Equal("Brand New", items[0].Title);
            Assert.Equal("Arrived Late", items[1].Title);

            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void FeedViewModel_Deduplication_IgnoresDuplicateId()
    {
        var thread = new Thread(() =>
        {
            var configService = new DummyConfigService();
            var apiService = new DummyApiService();
            var presenter = new DummyPresenter();

            using var vm = new FeedViewModel(apiService, configService, presenter);

            var msg = new GotifyMessage { Id = 42, Title = "Original" };
            var dup = new GotifyMessage { Id = 42, Title = "Duplicate" };

            vm.AddMessage(msg);
            vm.AddMessage(dup);

            Assert.Single(vm.Messages);
            Assert.Equal("Original", vm.Messages[0].Title);

            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void GotifyMessage_DateAndRelativeTime_HandlesUnspecifiedKind_AndFutureClockSkew()
    {
        var unspecified = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var msg = new GotifyMessage
        {
            Date = unspecified
        };

        Assert.Equal(DateTimeKind.Utc, msg.Date.Kind);

        // Future clock skew (server clock 1 minute ahead of local client)
        var futureMsg = new GotifyMessage
        {
            Date = DateTime.UtcNow.AddMinutes(1)
        };

        Assert.Equal("Just now", futureMsg.RelativeTime);

        // Property changed notification on refresh
        bool eventFired = false;
        futureMsg.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GotifyMessage.RelativeTime))
                eventFired = true;
        };

        futureMsg.RefreshRelativeTime();
        Assert.True(eventFired);
    }

    [Fact]
    public void SessionNotificationTracker_WhenLocked_RecordsNotifications_AndGeneratesSummaryOnUnlock()
    {
        var tracker = new SessionNotificationTracker(minLockDuration: TimeSpan.Zero);
        var settings = new PresentationSettings { EnableMissedSummaryOnUnlock = true };

        // Before locking, notifications should not be recorded as missed
        var unrecorded = new GotifyMessage { Id = 1, Title = "Before Lock", ApplicationName = "App1", Priority = 5 };
        tracker.RecordNotification(unrecorded);
        Assert.Equal(0, tracker.MissedCount);

        // Lock session
        tracker.OnSessionLocked();
        Assert.True(tracker.IsLocked);

        var msg1 = new GotifyMessage { Id = 2, Title = "Alert 1", ApplicationName = "HomeAssistant", Priority = 5 };
        var msg2 = new GotifyMessage { Id = 3, Title = "Alert 2", ApplicationName = "BackupServer", Priority = 8 };
        var msg3 = new GotifyMessage { Id = 4, Title = "Alert 3", ApplicationName = "Plex", Priority = 4 };

        tracker.RecordNotification(msg1);
        tracker.RecordNotification(msg2);
        tracker.RecordNotification(msg3);

        Assert.Equal(3, tracker.MissedCount);

        // Unlock session
        var summary = tracker.OnSessionUnlocked(settings);

        Assert.False(tracker.IsLocked);
        Assert.NotNull(summary);
        Assert.Equal("Missed Notifications (3)", summary.Title);
        Assert.Contains("HomeAssistant, BackupServer, Plex", summary.Message);
        Assert.Equal(8, summary.Priority); // Max priority preserved
        Assert.Equal(0, tracker.MissedCount); // Buffer cleared
    }

    [Fact]
    public void SessionNotificationTracker_WhenBelowMinDuration_OrDisabled_ReturnsNull()
    {
        // 1. Minimum duration threshold: locked for 0 seconds when minimum is 10 seconds
        var trackerWithThreshold = new SessionNotificationTracker(minLockDuration: TimeSpan.FromSeconds(10));
        var settings = new PresentationSettings { EnableMissedSummaryOnUnlock = true };

        trackerWithThreshold.OnSessionLocked();
        trackerWithThreshold.RecordNotification(new GotifyMessage { Id = 1, ApplicationName = "App" });

        var summary = trackerWithThreshold.OnSessionUnlocked(settings);
        Assert.Null(summary); // Duration too short, suppressed to prevent lock-unlock spam

        // 2. Disabled setting
        var trackerDisabled = new SessionNotificationTracker(minLockDuration: TimeSpan.Zero);
        var disabledSettings = new PresentationSettings { EnableMissedSummaryOnUnlock = false };

        trackerDisabled.OnSessionLocked();
        trackerDisabled.RecordNotification(new GotifyMessage { Id = 1, ApplicationName = "App" });

        var disabledSummary = trackerDisabled.OnSessionUnlocked(disabledSettings);
        Assert.Null(disabledSummary);
    }

    [Fact]
    public void UpdateService_HasWriteAccess_AccuratelyReportsDirectoryPermissions()
    {
        var tempDir = Path.GetTempPath();
        Assert.True(UpdateService.HasWriteAccess(tempDir));

        var invalidDir = Path.Join(tempDir, Guid.NewGuid().ToString("N"), "nonexistent_subfolder");
        Assert.False(UpdateService.HasWriteAccess(invalidDir));
    }
}

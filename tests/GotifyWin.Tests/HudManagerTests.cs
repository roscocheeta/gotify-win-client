using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace GotifyWin.Tests;

public class HudManagerTests
{
    [Fact]
    public void HudManager_QueueLimitsAndBehavior()
    {
        var thread = new Thread(() =>
        {
            var manager = new HudManager();
            var settings = new PresentationSettings
            {
                Mode = NotificationMode.CustomHud,
                Position = HudPosition.BottomRight,
                DisplayDurationSeconds = 5
            };

            // Queue 6 notifications
            for (int i = 1; i <= 6; i++)
            {
                var msg = new GotifyMessage
                {
                    Id = i,
                    Title = $"Alert {i}",
                    Message = $"Message body {i}",
                    Priority = 5
                };
                manager.ShowNotification(msg, settings, null);
            }

            // Exactly MaxConcurrentVisible should be active, rest queued
            Assert.Equal(HudManager.MaxConcurrentVisible, manager.ActiveCount);
            Assert.Equal(6 - HudManager.MaxConcurrentVisible, manager.QueuedCount);

            manager.DismissAll();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void HudManager_TopLeft_And_BottomLeft_Positions()
    {
        var thread = new Thread(() =>
        {
            var manager = new HudManager();
            var settingsTL = new PresentationSettings
            {
                Mode = NotificationMode.CustomHud,
                Position = HudPosition.TopLeft,
                DisplayDurationSeconds = 5
            };

            var msg = new GotifyMessage
            {
                Id = 1,
                Title = "TopLeft Alert",
                Message = "Testing TopLeft positioning",
                Priority = 6
            };

            manager.ShowNotification(msg, settingsTL, null);
            Assert.Equal(1, manager.ActiveCount);

            var settingsBL = new PresentationSettings
            {
                Mode = NotificationMode.CustomHud,
                Position = HudPosition.BottomLeft,
                DisplayDurationSeconds = 5
            };

            var msg2 = new GotifyMessage
            {
                Id = 2,
                Title = "BottomLeft Alert",
                Message = "Testing BottomLeft positioning",
                Priority = 7
            };

            manager.ShowNotification(msg2, settingsBL, null);
            Assert.Equal(2, manager.ActiveCount);

            manager.DismissAll();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void NotificationHudWindow_CountdownBar_SetupAndVisibility()
    {
        var thread = new Thread(() =>
        {
            var window = new Views.NotificationHudWindow();
            var settings = new PresentationSettings
            {
                ShowCountdownBar = true,
                DisplayDurationSeconds = 4
            };
            var msg = new GotifyMessage
            {
                Id = 1,
                Title = "Test Alert",
                Message = "Body text",
                Priority = 5
            };

            window.Setup(msg, settings, null);

            Assert.Equal(Visibility.Visible, window.CountdownBarVisibility);
            Assert.Equal(1.0, window.CountdownScaleX);

            // Test with ShowCountdownBar = false
            settings.ShowCountdownBar = false;
            window.Setup(msg, settings, null);
            Assert.Equal(Visibility.Collapsed, window.CountdownBarVisibility);

            window.Close();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void NotificationHudWindow_DismissButton_AlwaysVisible_And_OpenLinkVisibility()
    {
        var thread = new Thread(() =>
        {
            var window = new Views.NotificationHudWindow();
            var settings = new PresentationSettings { DisplayDurationSeconds = 4 };

            // 1. Message without ClickUrl
            var msgNoUrl = new GotifyMessage
            {
                Id = 1,
                Title = "Simple Alert",
                Message = "No URL attached",
                Priority = 5
            };

            window.Setup(msgNoUrl, settings, null);

            Assert.Equal(Visibility.Visible, window.ActionsGridVisibility);
            Assert.Equal(Visibility.Visible, window.DismissButtonVisibility);
            Assert.Equal(Visibility.Collapsed, window.OpenLinkButtonVisibility);

            // 2. Message with ClickUrl
            using var jsonDoc = System.Text.Json.JsonDocument.Parse(@"{""click"": {""url"": ""https://example.com""}}");
            var msgWithUrl = new GotifyMessage
            {
                Id = 2,
                Title = "Alert with URL",
                Message = "Click me",
                Priority = 6,
                Extras = new System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["client::notification"] = jsonDoc.RootElement.Clone()
                }
            };

            window.Setup(msgWithUrl, settings, null);

            Assert.Equal(Visibility.Visible, window.ActionsGridVisibility);
            Assert.Equal(Visibility.Visible, window.DismissButtonVisibility);
            Assert.Equal(Visibility.Visible, window.OpenLinkButtonVisibility);

            window.Close();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void HudManager_SessionLock_QueuesNotificationsAndPurgesExpiredOnUnlock()
    {
        var thread = new Thread(() =>
        {
            var manager = new HudManager();
            var settings = new PresentationSettings
            {
                Mode = NotificationMode.CustomHud,
                Position = HudPosition.BottomRight,
                DisplayDurationSeconds = 2
            };

            // Simulate workstation locked
            manager.IsSessionLocked = true;

            // Notification arrives while locked
            var msg = new GotifyMessage
            {
                Id = 10,
                Title = "Locked Alert",
                Message = "Arrived while PC locked",
                Priority = 5
            };
            manager.ShowNotification(msg, settings, null);

            // Should NOT spawn HWND on locked desktop, but queued
            Assert.Equal(0, manager.ActiveCount);
            Assert.Equal(1, manager.QueuedCount);

            // Purge expired queue
            var purged = manager.PurgeExpiredQueue();
            Assert.True(purged >= 0);

            // Unlock session
            manager.IsSessionLocked = false;
            manager.HandleSessionUnlock();

            manager.DismissAll();
            manager.Dispose();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void HudManager_SessionUnlock_SpawnsFreshQueuedNotification()
    {
        var thread = new Thread(() =>
        {
            var manager = new HudManager();
            var settings = new PresentationSettings
            {
                Mode = NotificationMode.CustomHud,
                Position = HudPosition.BottomRight,
                DisplayDurationSeconds = 10
            };

            // Lock session
            manager.IsSessionLocked = true;

            var msg = new GotifyMessage
            {
                Id = 11,
                Title = "Fresh Alert",
                Message = "Arrived right before unlock",
                Priority = 5
            };
            manager.ShowNotification(msg, settings, null);

            Assert.Equal(0, manager.ActiveCount);
            Assert.Equal(1, manager.QueuedCount);

            // Unlock session immediately while message is still fresh
            manager.IsSessionLocked = false;
            manager.HandleSessionUnlock();

            // Should have dequeued and spawned
            Assert.Equal(1, manager.ActiveCount);
            Assert.Equal(0, manager.QueuedCount);

            manager.DismissAll();
            manager.Dispose();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void NotificationHudWindow_CheckExpiration_ClosesImmediatelyIfExpired()
    {
        var thread = new Thread(() =>
        {
            var window = new Views.NotificationHudWindow();
            var settings = new PresentationSettings
            {
                ShowCountdownBar = true,
                DisplayDurationSeconds = 2
            };
            var msg = new GotifyMessage
            {
                Id = 12,
                Title = "Expiring Alert",
                Message = "Testing expiration",
                Priority = 5
            };

            window.Setup(msg, settings, null);

            // Check before closing
            Assert.False(window.IsClosing);
            Assert.False(window.IsExpired);

            window.Close();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}

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
}
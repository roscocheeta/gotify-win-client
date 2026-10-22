using System;
using System.Threading;
using System.Windows;
using GotifyWin.Models;
using GotifyWin.Models.Config;
using GotifyWin.Services;
using Xunit;

namespace GotifyWin.Tests;

public class HudManagerTests
{
    [Fact]
    public void HudManager_QueueLimitsAndBehavior()
    {
        var thread = new Thread(() =>
        {
            if (Application.Current == null)
            {
                new Application();
            }

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
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }
}

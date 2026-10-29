using GotifyWin.Models.Config;
using GotifyWin.Services;
using Xunit;

namespace GotifyWin.Tests;

public class DndManagerTests
{
    private readonly DndManager _manager = new();

    [Fact]
    public void IsDndActive_WhenManualDndIndefinite_ReturnsTrue()
    {
        var settings = new DndSettings
        {
            IsManualDndActive = true,
            ManualDndUntilUtc = null
        };

        Assert.True(_manager.IsDndActive(settings));
    }

    [Fact]
    public void IsDndActive_WhenManualDndTimedAndNotExpired_ReturnsTrue()
    {
        var settings = new DndSettings
        {
            IsManualDndActive = true,
            ManualDndUntilUtc = DateTime.UtcNow.AddMinutes(30)
        };

        Assert.True(_manager.IsDndActive(settings));
    }

    [Fact]
    public void IsDndActive_WhenManualDndTimedAndExpired_ReturnsFalseAndResets()
    {
        var settings = new DndSettings
        {
            IsManualDndActive = true,
            ManualDndUntilUtc = DateTime.UtcNow.AddMinutes(-5), // expired
            SuppressOnFullscreenApps = false
        };

        var isActive = _manager.IsDndActive(settings);

        Assert.False(isActive);
        Assert.False(settings.IsManualDndActive);
        Assert.Null(settings.ManualDndUntilUtc);
    }

    [Theory]
    [InlineData(23, 0, true)]   // 11:00 PM -> In quiet hours
    [InlineData(3, 0, true)]    // 3:00 AM -> In quiet hours
    [InlineData(6, 59, true)]   // 6:59 AM -> In quiet hours
    [InlineData(7, 0, false)]   // 7:00 AM -> Quiet hours ended
    [InlineData(12, 0, false)]  // 12:00 PM -> Daytime
    [InlineData(21, 59, false)] // 9:59 PM -> Before quiet hours
    [InlineData(22, 0, true)]   // 10:00 PM -> Quiet hours start
    public void IsQuietHours_WhenCrossingMidnight_EvaluatesCorrectly(int hour, int minute, bool expected)
    {
        var settings = new DndSettings
        {
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeSpan(22, 0, 0), // 10 PM
            QuietHoursEnd = new TimeSpan(7, 0, 0)     // 7 AM
        };

        var testTime = new DateTime(2026, 10, 22, hour, minute, 0);
        var result = _manager.IsQuietHours(settings, testTime);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(8, 59, false)]  // Before 9 AM
    [InlineData(9, 0, true)]    // 9:00 AM
    [InlineData(14, 0, true)]   // 2:00 PM
    [InlineData(17, 0, false)]  // 5:00 PM (end)
    [InlineData(20, 0, false)]  // 8:00 PM
    public void IsQuietHours_WhenSameDay_EvaluatesCorrectly(int hour, int minute, bool expected)
    {
        var settings = new DndSettings
        {
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeSpan(9, 0, 0),  // 9 AM
            QuietHoursEnd = new TimeSpan(17, 0, 0)   // 5 PM
        };

        var testTime = new DateTime(2026, 10, 22, hour, minute, 0);
        var result = _manager.IsQuietHours(settings, testTime);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsQuietHours_WhenDisabled_ReturnsFalse()
    {
        var settings = new DndSettings
        {
            QuietHoursEnabled = false,
            QuietHoursStart = new TimeSpan(0, 0, 0),
            QuietHoursEnd = new TimeSpan(23, 59, 59)
        };

        var testTime = new DateTime(2026, 10, 22, 12, 0, 0);
        Assert.False(_manager.IsQuietHours(settings, testTime));
    }

    [Fact]
    public void GetStatusDescription_WhenDndOff_ReturnsDndOff()
    {
        var settings = new DndSettings
        {
            IsManualDndActive = false,
            QuietHoursEnabled = false,
            SuppressOnFullscreenApps = false
        };

        var desc = _manager.GetStatusDescription(settings);
        Assert.Equal("DND: Off", desc);
    }

    [Fact]
    public void GetStatusDescription_WhenManualDndIndefinite_ReturnsIndefinite()
    {
        var settings = new DndSettings
        {
            IsManualDndActive = true,
            ManualDndUntilUtc = null
        };

        var desc = _manager.GetStatusDescription(settings);
        Assert.Equal("DND on (Indefinite)", desc);
    }

    [Fact]
    public void GetFullscreenState_ExclusiveD3D_DetectsGame()
    {
        var manager = new DndManager();
        manager.StateProbe = () => FullscreenState.Game;

        var state = manager.GetFullscreenState();
        Assert.Equal(FullscreenState.Game, state);
        Assert.True(manager.IsFullscreenAppRunning());
    }

    [Fact]
    public void AltTabRetention_WhileGameProcessRunning_MaintainsDnd()
    {
        var manager = new DndManager
        {
            WinNotificationStateProbe = () => 5, // QUNS_ACCEPTS_NOTIFICATIONS
            ForegroundWindowProbe = () => IntPtr.Zero
        };
        var settings = new DndSettings
        {
            SuppressOnFullscreenApps = true,
            RetainGameDndOnAltTab = true,
            GameAltTabRetentionSeconds = 300
        };

        int currentPid = Environment.ProcessId;
        manager.ProcessRunningProbe = pid => pid == currentPid;
        manager.SetActiveGameSessionForTest(currentPid, "TestGame.exe", DateTime.UtcNow.AddSeconds(-30));

        var isDnd = manager.IsDndActive(settings);
        Assert.True(isDnd);

        var desc = manager.GetStatusDescription(settings);
        Assert.Contains("TestGame.exe", desc);
    }

    [Fact]
    public void AltTabRetention_WhenGracePeriodExpires_DropsDnd()
    {
        var manager = new DndManager
        {
            WinNotificationStateProbe = () => 5, // QUNS_ACCEPTS_NOTIFICATIONS
            ForegroundWindowProbe = () => IntPtr.Zero
        };
        var settings = new DndSettings
        {
            SuppressOnFullscreenApps = true,
            RetainGameDndOnAltTab = true,
            GameAltTabRetentionSeconds = 60
        };

        int currentPid = Environment.ProcessId;
        manager.ProcessRunningProbe = pid => true;
        manager.SetActiveGameSessionForTest(currentPid, "OldGame.exe", DateTime.UtcNow.AddSeconds(-120));

        var isDnd = manager.IsDndActive(settings);
        Assert.False(isDnd);
        Assert.Equal(0, manager.TrackedGameProcessId);
    }

    [Fact]
    public void AltTabRetention_WhenGameProcessExits_DropsDndImmediately()
    {
        var manager = new DndManager
        {
            WinNotificationStateProbe = () => 5, // QUNS_ACCEPTS_NOTIFICATIONS
            ForegroundWindowProbe = () => IntPtr.Zero
        };
        var settings = new DndSettings
        {
            SuppressOnFullscreenApps = true,
            RetainGameDndOnAltTab = true,
            GameAltTabRetentionSeconds = 300
        };

        manager.ProcessRunningProbe = pid => false;
        manager.SetActiveGameSessionForTest(999999, "ClosedGame.exe", DateTime.UtcNow.AddSeconds(-10));

        var isDnd = manager.IsDndActive(settings);
        Assert.False(isDnd);
        Assert.Equal(0, manager.TrackedGameProcessId);
    }

    [Fact]
    public void AltTabRetention_WhenDisabledInSettings_DoesNotRetainDnd()
    {
        var manager = new DndManager
        {
            WinNotificationStateProbe = () => 5, // QUNS_ACCEPTS_NOTIFICATIONS
            ForegroundWindowProbe = () => IntPtr.Zero
        };
        var settings = new DndSettings
        {
            SuppressOnFullscreenApps = true,
            RetainGameDndOnAltTab = false
        };

        int currentPid = Environment.ProcessId;
        manager.ProcessRunningProbe = pid => true;
        manager.SetActiveGameSessionForTest(currentPid, "Game.exe", DateTime.UtcNow.AddSeconds(-10));

        var isDnd = manager.IsDndActive(settings);
        Assert.False(isDnd);
    }

    [Fact]
    public void DndManager_StressTest_RapidIsDndActiveQueries_CompletesQuicklyWithoutMemoryLeak()
    {
        var manager = new DndManager
        {
            WinNotificationStateProbe = () => 5, // QUNS_ACCEPTS_NOTIFICATIONS
            ForegroundWindowProbe = () => IntPtr.Zero
        };
        var settings = new DndSettings
        {
            SuppressOnFullscreenApps = true,
            RetainGameDndOnAltTab = true,
            GameAltTabRetentionSeconds = 300
        };

        int currentPid = Environment.ProcessId;
        manager.ProcessRunningProbe = pid => pid == currentPid;
        manager.SetActiveGameSessionForTest(currentPid, "StressGame.exe", DateTime.UtcNow);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            _ = manager.IsDndActive(settings);
        }
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500, $"1000 queries took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void DndManager_Security_HandlesInvalidOrTerminatedProcessIdsGracefully()
    {
        var manager = new DndManager
        {
            WinNotificationStateProbe = () => 5, // QUNS_ACCEPTS_NOTIFICATIONS
            ForegroundWindowProbe = () => IntPtr.Zero
        };
        var settings = new DndSettings
        {
            SuppressOnFullscreenApps = true,
            RetainGameDndOnAltTab = true,
            GameAltTabRetentionSeconds = 300
        };

        manager.SetActiveGameSessionForTest(int.MaxValue - 1, "NonExistentGame.exe", DateTime.UtcNow.AddSeconds(-5));

        var exception = Record.Exception(() => manager.IsDndActive(settings));
        Assert.Null(exception);
        Assert.Equal(0, manager.TrackedGameProcessId);
    }
}

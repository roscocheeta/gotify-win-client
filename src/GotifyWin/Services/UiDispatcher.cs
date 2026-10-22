using System;

namespace GotifyWin.Services;

public static class UiDispatcher
{
    public static void RunOnUI(Action action)
    {
        var app = System.Windows.Application.Current;
        var dispatcher = app?.Dispatcher;

        if (dispatcher == null || dispatcher.CheckAccess() || !dispatcher.Thread.IsAlive || dispatcher.HasShutdownStarted)
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}

using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace GotifyWin.Services;

public class StartupManager : IStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GotifyWinClient";

    public bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    // SEC-13: Handle framework-dependent dotnet.exe launch vs published standalone executable
                    if (Path.GetFileName(exePath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var dllPath = Path.Combine(AppContext.BaseDirectory, "GotifyWin.dll");
                        key.SetValue(AppName, $"\"{exePath}\" \"{dllPath}\" --minimized", RegistryValueKind.String);
                    }
                    else
                    {
                        key.SetValue(AppName, $"\"{exePath}\" --minimized", RegistryValueKind.String);
                    }
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartupManager] SetAutoStart error: {ex.Message}");
        }
    }
}

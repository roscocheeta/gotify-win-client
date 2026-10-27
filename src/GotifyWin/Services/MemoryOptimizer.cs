using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GotifyWin.Services;

/// <summary>
/// Provides routines to compact heap memory and trim working set pages
/// when the client transitions into a passive background or minimized-to-tray state.
/// </summary>
public static class MemoryOptimizer
{
    [DllImport("kernel32.dll", EntryPoint = "SetProcessWorkingSetSize", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minSize, IntPtr maxSize);

    /// <summary>
    /// Forces full generational GC collection and returns unneeded physical pages
    /// from the working set to the operating system.
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

            if (OperatingSystem.IsWindows())
            {
                using var currentProcess = Process.GetCurrentProcess();
                SetProcessWorkingSetSize(currentProcess.Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoryOptimizer] Working set trim error: {ex.Message}");
        }
    }
}

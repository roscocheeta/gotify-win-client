using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace GotifyWin.Services;

/// <summary>
/// Manages Windows AppUserModelId (AUMID) and Start Menu shortcut registration.
/// Windows 10/11 requires unpackaged Win32 apps to possess an explicit AUMID and a
/// Start Menu shortcut (.lnk) with PKEY_AppUserModel_ID written to its COM IPropertyStore.
/// Without this, ToastNotificationManager silently discards notifications.
/// </summary>
public static class AumidHelper
{
    public const string AppUserModelId = "GotifyWin.DesktopClient";
    public const string ShortcutFileName = "Gotify Desktop.lnk";

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    public static void Initialize()
    {
        try
        {
            // 1. Explicitly associate current process with the AUMID
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

            // 2. Ensure Start Menu shortcut exists and has PKEY_AppUserModel_ID set
            EnsureShortcut();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AumidHelper] Initialization notice: {ex.Message}");
        }
    }

    public static void EnsureShortcut()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath) ||
                exePath.EndsWith("testhost.exe", StringComparison.OrdinalIgnoreCase))
            {
                // Do not register or modify shortcuts during test runs
                return;
            }

            var startMenuDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs"
            );

            if (!Directory.Exists(startMenuDir))
            {
                Directory.CreateDirectory(startMenuDir);
            }

            var shortcutPath = Path.Combine(startMenuDir, ShortcutFileName);

            // Create or update shortcut using COM IShellLink and IPropertyStore
            var shellLink = (IShellLinkW)new ShellLink();
            shellLink.SetPath(exePath);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? string.Empty);
            shellLink.SetDescription("Gotify Desktop Client");
            shellLink.SetIconLocation(exePath, 0);

            var propertyStore = (IPropertyStore)shellLink;
            var propKey = new PROPERTYKEY
            {
                fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                pid = 5
            };

            var propVar = new PropVariant(AppUserModelId);
            try
            {
                propertyStore.SetValue(ref propKey, ref propVar);
                propertyStore.Commit();
            }
            finally
            {
                propVar.Clear();
            }

            var persistFile = (IPersistFile)shellLink;
            persistFile.Save(shortcutPath, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AumidHelper] EnsureShortcut error: {ex.Message}");
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PropVariant pv);
        void SetValue(ref PROPERTYKEY key, ref PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pwszVal;

        public PropVariant(string str)
        {
            vt = 31; // VT_LPWSTR
            pwszVal = Marshal.StringToCoTaskMemUni(str);
        }

        public void Clear()
        {
            if (pwszVal != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pwszVal);
                pwszVal = IntPtr.Zero;
            }
            vt = 0;
        }
    }
}

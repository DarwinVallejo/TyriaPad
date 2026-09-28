using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

using TyriaPad.Core.Diagnostics;

namespace TyriaPad.App;

/// <summary>
/// "Start with Windows": a shortcut in the user's Startup folder
/// (<c>shell:startup</c>), with no registry entries or scheduled tasks. It can be seen and deleted from
/// Explorer, and Windows disables it from Settings → Apps → Startup.
/// </summary>
internal static class StartupShortcut
{
    /// <summary>Argument the shortcut starts with: without GW2 open, TyriaPad stays idle.</summary>
    public const string Argument = "--autostart";

    private static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "TyriaPad.lnk");

    private static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TyriaPad.exe");

    /// <summary>The shortcut exists and points to this executable (not to another copy).</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                return File.Exists(ShortcutPath) && string.Equals(ReadTarget(ShortcutPath), ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>Creates or deletes the shortcut. Returns false (and logs it) if Windows does not allow it.</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var link = (IShellLinkW)new ShellLink();
                try
                {
                    link.SetPath(ExecutablePath);
                    link.SetArguments(Argument);
                    link.SetWorkingDirectory(AppContext.BaseDirectory);
                    link.SetDescription("TyriaPad: controller for Guild Wars 2 (idle until you start the game)");
                    ((IPersistFile)link).Save(ShortcutPath, true);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(link);
                }

                Log.Write($"Start with Windows: created {ShortcutPath}");
            }
            else if (File.Exists(ShortcutPath))
            {
                File.Delete(ShortcutPath);
                Log.Write($"Start with Windows: deleted {ShortcutPath}");
            }

            return true;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"Start with Windows: could not {(enabled ? "create" : "delete")} the shortcut: {ex.Message}");
            return false;
        }
    }

    private static string ReadTarget(string path)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, 0, 0);
            return target.ToString();
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, nint findData, uint flags);

        void GetIDList(out nint idList);

        void SetIDList(nint idList);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCmd);

        void SetShowCmd(int showCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int icon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

        void Resolve(nint hwnd, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}

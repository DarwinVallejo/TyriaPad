using System.Runtime.InteropServices;

using TyriaPad.Core.Native;

namespace TyriaPad.Core.Game;

/// <summary>
/// Checks whether the Windows cursor is visible. With Action Camera GW2 hides it, and shows it again
/// when a menu opens, when talking to an NPC or when leaving Action Camera. It's system state, not
/// game memory.
/// </summary>
public static class CursorProbe
{
    /// <returns>null if Windows couldn't answer.</returns>
    public static bool? IsCursorVisible()
    {
        var info = new User32.CursorInfo { Size = (uint)Marshal.SizeOf<User32.CursorInfo>() };
        if (!User32.GetCursorInfo(ref info))
        {
            return null;
        }

        // "Suppressed" = Windows hides it because the touch screen was used, but the game shows it.
        bool shown = (info.Flags & (User32.CursorShowing | User32.CursorSuppressed)) != 0;
        return shown && info.Cursor != 0;
    }
}
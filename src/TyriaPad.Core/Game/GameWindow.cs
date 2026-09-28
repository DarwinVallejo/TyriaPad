using TyriaPad.Core.Native;

namespace TyriaPad.Core.Game;

/// <summary>Rectangle in physical screen pixels.</summary>
public readonly record struct WindowBounds(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>Queries the position of a window's client area (where GW2 draws), without touching the game.</summary>
public static class GameWindow
{
    /// <returns>null if the window no longer exists, is minimized or has no client area.</returns>
    public static WindowBounds? TryGetClientBounds(nint hwnd)
    {
        if (hwnd == 0 || !User32.IsWindow(hwnd) || User32.IsIconic(hwnd))
        {
            return null;
        }

        if (!User32.GetClientRect(hwnd, out User32.Rect rect))
        {
            return null;
        }

        var origin = default(User32.Point);
        if (!User32.ClientToScreen(hwnd, ref origin))
        {
            return null;
        }

        var bounds = new WindowBounds(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return bounds.IsEmpty ? null : bounds;
    }

    /// <summary>
    /// Windows DPI scale on the window's monitor (1 = 100%, 1.5 = 150%). GW2 scales its interface
    /// by this factor when its "DPI Scaling" option is on (it is by default).
    /// </summary>
    public static float GetDpiScale(nint hwnd)
    {
        uint dpi = hwnd == 0 ? 0 : User32.GetDpiForWindow(hwnd);
        return dpi == 0 ? 1f : dpi / 96f;
    }
}

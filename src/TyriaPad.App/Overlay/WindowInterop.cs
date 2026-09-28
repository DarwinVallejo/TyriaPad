using System.Runtime.InteropServices;

namespace TyriaPad.App.Overlay;

/// <summary>The bare minimum of user32 to place the overlay window and make it click-through.</summary>
internal static partial class WindowInterop
{
    public const int GwlExStyle = -20;
    public const nint WsExTransparent = 0x0000_0020;
    public const nint WsExToolWindow = 0x0000_0080;
    public const nint WsExNoActivate = 0x0800_0000;

    public static readonly nint HwndTopmost = -1;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;

    public const int SmCxScreen = 0;
    public const int SmCyScreen = 1;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    public static void AddExStyle(nint hwnd, nint style)
        => SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle) | style);

    public static void RemoveExStyle(nint hwnd, nint style)
        => SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle) & ~style);
}

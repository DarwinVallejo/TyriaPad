using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

using TyriaPad.Core.Game;

namespace TyriaPad.App.Overlay;

/// <summary>
/// Transparent window, always on top and never activated, placed in physical pixels over GW2's
/// client area. By default it lets clicks through (WS_EX_TRANSPARENT); during calibration it
/// receives them. It doesn't appear in the taskbar or in Alt+Tab (WS_EX_TOOLWINDOW).
/// </summary>
internal sealed class OverlayWindow : Window
{
    private nint _hwnd;
    private bool _clickThrough = true;
    private WindowBounds? _placed;

    public OverlayWindow()
    {
        Title = "TyriaPad overlay";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;

        var root = new Grid();
        root.Children.Add(Canvas);
        root.Children.Add(Calibration);
        Calibration.Visibility = Visibility.Collapsed;
        Content = root;
    }

    public OverlayCanvas Canvas { get; } = new() { IsHitTestVisible = false };

    public CalibrationLayer Calibration { get; } = new();

    /// <summary>DPI scale of the monitor the window is on (physical pixels per WPF unit).</summary>
    public double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            _clickThrough = value;
            if (_hwnd != 0)
            {
                ApplyClickThrough();
            }
        }
    }

    /// <summary>Shows the window (without activating it) over these screen coordinates.</summary>
    public void Place(WindowBounds bounds)
    {
        if (!IsVisible)
        {
            Show();
            _placed = null;
        }

        if (_placed == bounds)
        {
            return;
        }

        _placed = bounds;
        WindowInterop.SetWindowPos(_hwnd, WindowInterop.HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, WindowInterop.SwpNoActivate | WindowInterop.SwpShowWindow);
    }

    public void HideOverlay()
    {
        if (IsVisible)
        {
            Hide();
            _placed = null;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowInterop.AddExStyle(_hwnd, WindowInterop.WsExToolWindow | WindowInterop.WsExNoActivate);
        ApplyClickThrough();
    }

    private void ApplyClickThrough()
    {
        if (_clickThrough)
        {
            WindowInterop.AddExStyle(_hwnd, WindowInterop.WsExTransparent);
        }
        else
        {
            WindowInterop.RemoveExStyle(_hwnd, WindowInterop.WsExTransparent);
        }
    }
}

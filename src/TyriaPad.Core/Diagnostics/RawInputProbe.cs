using System.Runtime.InteropServices;

using TyriaPad.Core.Native;

namespace TyriaPad.Core.Diagnostics;

/// <summary>
/// Diagnostic (<c>--rawinput-probe</c>): logs the raw HID reports that arrive through Raw Input with
/// <c>RIDEV_INPUTSINK</c> from gamepad, joystick, multi-axis and, as a control, the Ally X vendor
/// collection (page FF31, usage 76). It confirmed that in the Xbox full screen experience the
/// controller arrives through Raw Input even though XInput and GameInput report it idle. It registers
/// the same collections as <see cref="Input.RawInputGamepad"/> (Windows allows one window per collection
/// and process), so while it runs the controller isn't read through Raw Input. Read-only.
/// </summary>
public sealed unsafe class RawInputProbe : IDisposable
{
    private const int MaxLoggedReports = 400;
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(10);

    // (page, usage): gamepad, joystick, multi-axis and the ASUS buttons collection.
    private static readonly (ushort Page, ushort Usage, string Name)[] s_collections =
    [
        (0x01, 0x05, "gamepad"),
        (0x01, 0x04, "joystick"),
        (0x01, 0x08, "multi-axis"),
        (0xFF31, 0x76, "ASUS (M1/M2, control)"),
    ];

    private static RawInputProbe? s_instance;

    private readonly Thread _thread;
    private readonly Timer _summaryTimer;
    private readonly Lock _lock = new();
    private readonly Dictionary<nint, DeviceStats> _devices = [];
    private nint _window;
    private int _loggedReports;

    public RawInputProbe()
    {
        s_instance = this;
        List<nint> devices = RawInput.ListHidDevices();
        foreach (nint device in devices)
        {
            Log.Write($"RawInput: HID device {RawInput.Describe(device)}");
        }

        Log.Write($"RawInput: {devices.Count} HID devices");
        _thread = new Thread(Run) { Name = "TyriaPad.RawInputProbe", IsBackground = true };
        _thread.Start();
        _summaryTimer = new Timer(_ => LogSummary(), null, SummaryInterval, SummaryInterval);
    }

    public void Dispose()
    {
        _summaryTimer.Dispose();
        if (_window != 0)
        {
            RawInput.PostMessageW(_window, RawInput.WmClose, 0, 0);
        }

        _thread.Join(TimeSpan.FromSeconds(1));
        s_instance = null;
    }

    private void Run()
    {
        _window = RawInput.CreateMessageWindow("TyriaPadRawInputProbe", &WndProc);
        if (_window == 0)
        {
            Log.Write($"RawInput: could not create the window (error {Marshal.GetLastPInvokeError()})");
            return;
        }

        foreach ((ushort page, ushort usage, string name) in s_collections)
        {
            var device = new RawInput.RawInputDevice { UsagePage = page, Usage = usage, Flags = RawInput.RidevInputSink | RawInput.RidevDevNotify, Target = _window };
            bool ok = RawInput.RegisterRawInputDevices(&device, 1, (uint)sizeof(RawInput.RawInputDevice));
            Log.Write($"RawInput: register {name} (page {page:X2}, usage {usage:X2}) with INPUTSINK: {(ok ? "ok" : $"failed (error {Marshal.GetLastPInvokeError()})")}");
        }

        RawInput.RunMessageLoop();
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case RawInput.WmInput:
                    s_instance?.OnInput(lParam);
                    break;
                case RawInput.WmInputDeviceChange:
                    Log.Write($"RawInput: device {(wParam == RawInput.GidcArrival ? "connected" : "disconnected")}: {RawInput.Describe(lParam)}");
                    break;
                case RawInput.WmClose:
                    RawInput.DestroyWindow(hwnd);
                    return 0;
                case RawInput.WmDestroy:
                    RawInput.PostQuitMessage(0);
                    return 0;
            }
        }
        catch (Exception ex)
        {
            // An exception can't escape from here: it would cross native code and kill the process.
            Log.Write($"RawInput: read error: {ex.Message}");
        }

        return RawInput.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void OnInput(nint handle)
    {
        if (RawInput.ReadInput(handle, out nint device) is not { } input)
        {
            return;
        }

        ReadOnlySpan<byte> report = RawInput.LastReport(input);
        if (report.IsEmpty)
        {
            return;
        }

        string hex = Convert.ToHexString(report);
        string? line = null;
        lock (_lock)
        {
            if (!_devices.TryGetValue(device, out DeviceStats? stats))
            {
                stats = new DeviceStats(RawInput.Describe(device));
                _devices[device] = stats;
                line = $"RawInput: first report from {stats.Name}: {hex}";
            }

            stats.Reports++;
            if (hex != stats.Last)
            {
                stats.Changes++;
                stats.Last = hex;
                if (line is null && _loggedReports < MaxLoggedReports)
                {
                    _loggedReports++;
                    line = $"RawInput: {stats.Name}: {hex}{(_loggedReports == MaxLoggedReports ? " (limit reached; only counting from now on)" : "")}";
                }
            }
        }

        if (line is not null)
        {
            Log.Write(line);
        }
    }

    private void LogSummary()
    {
        string foreground = ForegroundName();
        lock (_lock)
        {
            if (_devices.Count == 0)
            {
                Log.Write($"RawInput: no reports in 10 s (foreground: {foreground})");
                return;
            }

            foreach (DeviceStats stats in _devices.Values)
            {
                Log.Write($"RawInput: {stats.Reports} reports in 10 s ({stats.Changes} distinct) from {stats.Name}; last {stats.Last} (foreground: {foreground})");
                stats.Reports = 0;
                stats.Changes = 0;
            }
        }
    }

    private static string ForegroundName()
    {
        User32.GetWindowThreadProcessId(User32.GetForegroundWindow(), out uint pid);
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"pid {pid}";
        }
    }

    private sealed class DeviceStats(string name)
    {
        public string Name { get; } = name;

        public int Reports { get; set; }

        public int Changes { get; set; }

        public string Last { get; set; } = string.Empty;
    }
}

using System.Runtime.InteropServices;

using TyriaPad.Core.Diagnostics;
using TyriaPad.Core.Native;

namespace TyriaPad.Core.Input;

/// <summary>
/// Reads the controller through Raw Input (HID gamepad collection) with <c>RIDEV_INPUTSINK</c>. In the
/// Xbox full screen experience it's the only driver-free path that delivers the controller to a
/// background app: XInput and GameInput report it idle (verified on the Ally X). Read-only: hides or
/// reprograms nothing. Reports arrive on a dedicated thread with a message loop and are decoded with
/// the device's HID descriptor (<see cref="HidGamepadDecoder"/>); <see cref="TryRead"/> gives the latest.
/// </summary>
public sealed unsafe class RawInputGamepad : IGamepadSource, IDisposable
{
    private static RawInputGamepad? s_instance;

    private readonly Thread _thread;
    private readonly Lock _lock = new();
    private readonly Dictionary<nint, Device> _devices = [];
    private nint _window;
    private RawGamepad _state;
    private nint _stateDevice;
    private bool _disposed;

    public RawInputGamepad()
    {
        s_instance = this;
        _thread = new Thread(Run) { Name = "TyriaPad.RawInputGamepad", IsBackground = true };
        _thread.Start();
    }

    /// <summary>The controller being read has LT and RT on a single axis (they cancel out if pressed together).</summary>
    public bool CombinedTriggers
    {
        get
        {
            lock (_lock)
            {
                return _devices.GetValueOrDefault(_stateDevice)?.Layout.CombinedTriggers == true;
            }
        }
    }

    /// <summary>An HID controller is connected. The reading is the last one received (neutral until the first report).</summary>
    public bool TryRead(out RawGamepad gamepad)
    {
        lock (_lock)
        {
            gamepad = _state;
            return _devices.Count > 0;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_window != 0)
        {
            RawInput.PostMessageW(_window, RawInput.WmClose, 0, 0);
        }

        _thread.Join(TimeSpan.FromSeconds(1));
        lock (_lock)
        {
            foreach (Device device in _devices.Values)
            {
                device.Dispose();
            }

            _devices.Clear();
        }

        if (ReferenceEquals(s_instance, this))
        {
            s_instance = null;
        }
    }

    private void Run()
    {
        _window = RawInput.CreateMessageWindow("TyriaPadRawInputGamepad", &WndProc);
        if (_window == 0)
        {
            Log.Write($"Raw Input controller: could not create the window (error {Marshal.GetLastPInvokeError()})");
            return;
        }

        foreach (nint handle in RawInput.ListHidDevices())
        {
            AddDevice(handle);
        }

        // Gamepad and joystick; DEVNOTIFY reports connections and disconnections.
        RawInput.RawInputDevice* registration = stackalloc RawInput.RawInputDevice[2];
        registration[0] = new RawInput.RawInputDevice { UsagePage = 0x01, Usage = 0x05, Flags = RawInput.RidevInputSink | RawInput.RidevDevNotify, Target = _window };
        registration[1] = new RawInput.RawInputDevice { UsagePage = 0x01, Usage = 0x04, Flags = RawInput.RidevInputSink | RawInput.RidevDevNotify, Target = _window };
        if (!RawInput.RegisterRawInputDevices(registration, 2, (uint)sizeof(RawInput.RawInputDevice)))
        {
            Log.Write($"Raw Input controller: could not register (error {Marshal.GetLastPInvokeError()})");
            return;
        }

        if (!_disposed)
        {
            RawInput.RunMessageLoop();
        }
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
                    if (wParam == RawInput.GidcArrival)
                    {
                        s_instance?.AddDevice(lParam);
                    }
                    else
                    {
                        s_instance?.RemoveDevice(lParam);
                    }

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
            Log.Write($"Raw Input controller: error: {ex}");
        }

        return RawInput.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void AddDevice(nint handle)
    {
        if (RawInput.GetDeviceInfo(handle) is not { UsagePage: 0x01, Usage: 0x04 or 0x05 })
        {
            return;
        }

        lock (_lock)
        {
            if (_devices.ContainsKey(handle))
            {
                return;
            }
        }

        if (Device.Open(handle) is not { } device)
        {
            Log.Write($"Raw Input controller: cannot read the descriptor of {RawInput.Describe(handle)}");
            return;
        }

        lock (_lock)
        {
            _devices[handle] = device;
        }

        HidGamepadLayout l = device.Layout;
        Log.Write($"Raw Input controller: {RawInput.Describe(handle)}; axes {Axes(l)}"
            + (l.CombinedTriggers ? "; LT and RT share the Z axis (pressed together they cancel out)" : string.Empty));
    }

    private void RemoveDevice(nint handle)
    {
        lock (_lock)
        {
            if (_devices.Remove(handle, out Device? device))
            {
                device.Dispose();
                if (_stateDevice == handle)
                {
                    _state = default;
                    _stateDevice = 0;
                }

                Log.Write("Raw Input controller: disconnected");
            }
        }
    }

    private void OnInput(nint handle)
    {
        if (RawInput.ReadInput(handle, out nint deviceHandle) is not { } input)
        {
            return;
        }

        Device? device;
        lock (_lock)
        {
            device = _devices.GetValueOrDefault(deviceHandle);
        }

        if (device is null)
        {
            // Arrived before the connection notice.
            AddDevice(deviceHandle);
            lock (_lock)
            {
                device = _devices.GetValueOrDefault(deviceHandle);
            }
        }

        ReadOnlySpan<byte> report = RawInput.LastReport(input);
        if (device is null || report.IsEmpty || device.Decode(report) is not { } state)
        {
            return;
        }

        lock (_lock)
        {
            _state = state;
            _stateDevice = deviceHandle;
        }
    }

    private static string Axes(HidGamepadLayout l)
    {
        var names = new List<string>(7);
        void Add(string name, HidAxisRange? range)
        {
            if (range is { } r)
            {
                names.Add($"{name} {r.Min}..{r.Max}");
            }
        }

        Add("X", l.X);
        Add("Y", l.Y);
        Add("Z", l.Z);
        Add("Rx", l.Rx);
        Add("Ry", l.Ry);
        Add("Rz", l.Rz);
        Add("hat", l.Hat);
        return string.Join(", ", names);
    }

    /// <summary>An HID controller: its descriptor (preparsed data) and the axis layout it declares.</summary>
    private sealed class Device : IDisposable
    {
        private readonly void* _preparsed;
        private readonly ushort[] _usages = new ushort[32];

        private Device(void* preparsed, HidGamepadLayout layout)
        {
            _preparsed = preparsed;
            Layout = layout;
        }

        public HidGamepadLayout Layout { get; }

        public static Device? Open(nint handle)
        {
            uint size = 0;
            RawInput.GetRawInputDeviceInfoW(handle, RawInput.RidiPreparsedData, null, &size);
            if (size is 0 or > 65536)
            {
                return null;
            }

            void* preparsed = NativeMemory.Alloc(size);
            if ((int)RawInput.GetRawInputDeviceInfoW(handle, RawInput.RidiPreparsedData, preparsed, &size) <= 0)
            {
                NativeMemory.Free(preparsed);
                return null;
            }

            RawInput.HidpCaps caps;
            if (RawInput.HidP_GetCaps(preparsed, &caps) != RawInput.HidpStatusSuccess || caps.NumberInputValueCaps == 0)
            {
                NativeMemory.Free(preparsed);
                return null;
            }

            ushort length = caps.NumberInputValueCaps;
            var values = new RawInput.HidpValueCaps[length];
            fixed (RawInput.HidpValueCaps* pointer = values)
            {
                RawInput.HidP_GetValueCaps(RawInput.HidpInput, pointer, &length, preparsed);
            }

            int count = Math.Min(length, values.Length);

            HidAxisRange? Find(ushort usage)
            {
                foreach (RawInput.HidpValueCaps v in values.AsSpan(0, count))
                {
                    bool matches = v.IsRange != 0 ? usage >= v.Usage && usage <= v.UsageMax : v.Usage == usage;
                    if (v.UsagePage == 0x01 && matches)
                    {
                        return HidAxisRange.FromCaps(v.LogicalMin, v.LogicalMax, v.BitSize);
                    }
                }

                return null;
            }

            var layout = new HidGamepadLayout(Find(0x30), Find(0x31), Find(0x32), Find(0x33), Find(0x34), Find(0x35), Find(0x39));
            return new Device(preparsed, layout);
        }

        public RawGamepad? Decode(ReadOnlySpan<byte> report)
        {
            fixed (byte* data = report)
            fixed (ushort* usages = _usages)
            {
                uint length = (uint)_usages.Length;
                int status = RawInput.HidP_GetUsages(RawInput.HidpInput, 0x09, 0, usages, &length, _preparsed, data, (uint)report.Length);
                if (status != RawInput.HidpStatusSuccess)
                {
                    length = 0;
                }

                void* preparsed = _preparsed;
                uint reportLength = (uint)report.Length;
                byte* bytes = data;
                uint? Axis(ushort usage)
                {
                    uint value;
                    return RawInput.HidP_GetUsageValue(RawInput.HidpInput, 0x01, 0, usage, &value, preparsed, bytes, reportLength) == RawInput.HidpStatusSuccess
                        ? value
                        : null;
                }

                return HidGamepadDecoder.Decode(Layout, new ReadOnlySpan<ushort>(usages, (int)length), Axis);
            }
        }

        public void Dispose() => NativeMemory.Free(_preparsed);
    }
}

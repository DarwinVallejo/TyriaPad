using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

using TyriaPad.Core.Native;

namespace TyriaPad.Core.Diagnostics;

/// <summary>
/// Diagnostic (<c>--hid-probe</c>): lists the HID devices, opens read-only the ASUS ones and those
/// that declare themselves a controller, and logs the reports that arrive. Hides or blocks nothing.
/// Result on the Ally X: the gamepad collection delivers no reports (the ASUS driver turns them
/// into XInput), but the vendor buttons one (page FF31, usage 76) does, even with GW2 in the
/// foreground. That's the path for M1/M2 in phase 5.
/// </summary>
public sealed class HidProbe : IDisposable
{
    private const ushort AsusVendorId = 0x0B05;
    private const ushort GenericDesktopPage = 0x01;
    private const ushort JoystickUsage = 0x04;
    private const ushort GamepadUsage = 0x05;

    // Cap on lines per device so the log doesn't grow unchecked.
    private const int MaxLoggedReports = 300;
    private static readonly TimeSpan MinLogInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(10);

    private readonly List<Reader> _readers = [];
    private readonly Timer _summaryTimer;

    public HidProbe()
    {
        int index = 0;
        foreach (string path in HidDevices.EnumeratePaths())
        {
            index++;
            HidDeviceInfo? info = HidDevices.Describe(path);
            if (info is null)
            {
                Log.Write($"HID {index}: could not query ({Marshal.GetLastPInvokeError()}) {path}");
                continue;
            }

            bool candidate = info.VendorId == AsusVendorId
                || (info.UsagePage == GenericDesktopPage && info.Usage is JoystickUsage or GamepadUsage);
            Log.Write($"HID {index}: VID {info.VendorId:X4} PID {info.ProductId:X4}, page {info.UsagePage:X2} usage {info.Usage:X2}, "
                + $"report {info.InputReportLength} B, \"{info.Product}\"{(candidate ? " → listening" : "")}");

            if (candidate && info.InputReportLength > 0)
            {
                TryStartReader(index, path, info);
            }
        }

        Log.Write($"HID: {index} devices, {_readers.Count} listening");
        _summaryTimer = new Timer(_ => LogSummary(), null, SummaryInterval, SummaryInterval);
    }

    public void Dispose()
    {
        _summaryTimer.Dispose();
        foreach (Reader reader in _readers)
        {
            reader.Dispose();
        }
    }

    private void TryStartReader(int index, string path, HidDeviceInfo info)
    {
        SafeFileHandle handle = HidDevices.OpenForRead(path);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            Log.Write($"HID {index}: cannot open for reading (error {error}{(error is 5 or 32 ? ", in exclusive use" : "")})");
            return;
        }

        _readers.Add(new Reader(index, info, handle));
    }

    private void LogSummary()
    {
        foreach (Reader reader in _readers)
        {
            Log.Write($"HID {reader.Index}: {reader.TakeCount()} reports in 10 s");
        }
    }

    /// <summary>Reads one device on its own thread with a blocking ReadFile.</summary>
    private sealed class Reader : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private readonly int _length;
        private readonly Thread _thread;
        private volatile bool _stopping;
        private int _count;

        public Reader(int index, HidDeviceInfo info, SafeFileHandle handle)
        {
            Index = index;
            _handle = handle;
            _length = info.InputReportLength;
            _thread = new Thread(Run) { Name = $"TyriaPad.HidProbe.{index}", IsBackground = true };
            _thread.Start();
        }

        public int Index { get; }

        public int TakeCount() => Interlocked.Exchange(ref _count, 0);

        public void Dispose()
        {
            _stopping = true;
            Hid.CancelIoEx(_handle, 0);
            _thread.Join(TimeSpan.FromSeconds(1));
            _handle.Dispose();
        }

        private unsafe void Run()
        {
            byte[] buffer = new byte[_length];
            byte[] previous = [];
            long lastLogged = 0;
            int logged = 0;

            while (!_stopping)
            {
                uint read;
                bool ok;
                fixed (byte* pointer = buffer)
                {
                    ok = Hid.ReadFile(_handle, pointer, (uint)_length, out read, 0);
                }

                if (!ok)
                {
                    if (!_stopping)
                    {
                        Log.Write($"HID {Index}: read failed (error {Marshal.GetLastPInvokeError()}), no longer listening");
                    }

                    return;
                }

                Interlocked.Increment(ref _count);
                ReadOnlySpan<byte> report = buffer.AsSpan(0, (int)read);
                if (logged < MaxLoggedReports && !report.SequenceEqual(previous)
                    && Stopwatch.GetElapsedTime(lastLogged) >= MinLogInterval)
                {
                    previous = report.ToArray();
                    lastLogged = Stopwatch.GetTimestamp();
                    logged++;
                    Log.Write($"HID {Index}: {Convert.ToHexString(report)}{(logged == MaxLoggedReports ? " (limit reached; only counting from now on)" : "")}");
                }
            }
        }
    }
}
using Microsoft.Win32.SafeHandles;

namespace TyriaPad.Core.Native;

/// <summary>
/// Waits with ~1 ms precision without raising the system clock resolution (timeBeginPeriod).
/// If the high-resolution timer doesn't exist (Windows older than 10 1803), uses Thread.Sleep.
/// </summary>
internal sealed class HighResolutionTimer : IDisposable
{
    private readonly SafeWaitHandle? _handle;

    public HighResolutionTimer()
    {
        SafeWaitHandle handle = Kernel32.CreateWaitableTimerEx(0, null, Kernel32.CreateWaitableTimerHighResolution, Kernel32.TimerAllAccess);
        if (handle.IsInvalid)
        {
            handle.Dispose();
        }
        else
        {
            _handle = handle;
        }
    }

    public void Sleep(TimeSpan duration)
    {
        // Negative value = relative time, in 100 ns units (same as TimeSpan.Ticks).
        long dueTime = -duration.Ticks;
        if (_handle is not null && Kernel32.SetWaitableTimer(_handle, in dueTime, 0, 0, 0, false))
        {
            Kernel32.WaitForSingleObject(_handle, Kernel32.Infinite);
        }
        else
        {
            Thread.Sleep(duration);
        }
    }

    public void Dispose() => _handle?.Dispose();
}
using System.Diagnostics;

namespace TyriaPad.Core.Diagnostics;

/// <summary>
/// CPU and memory of the process itself between two calls, for the log heartbeat. CPU is a
/// percentage of the whole machine (like Task Manager), and the peak seen is kept.
/// </summary>
public sealed class ResourceUsage
{
    private readonly Lock _lock = new();
    private TimeSpan _lastCpu;
    private long _lastTimestamp;
    private double _peakCpu;
    private long _peakPrivate;

    public ResourceUsage()
    {
        using Process self = Process.GetCurrentProcess();
        _lastCpu = self.TotalProcessorTime;
        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    public string Sample()
    {
        using Process self = Process.GetCurrentProcess();
        lock (_lock)
        {
            TimeSpan cpu = self.TotalProcessorTime;
            long now = Stopwatch.GetTimestamp();
            double elapsed = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalMilliseconds;
            double percent = elapsed > 0 ? (cpu - _lastCpu).TotalMilliseconds / elapsed / Environment.ProcessorCount * 100 : 0;
            _lastCpu = cpu;
            _lastTimestamp = now;

            long privateBytes = self.PrivateMemorySize64;
            _peakCpu = Math.Max(_peakCpu, percent);
            _peakPrivate = Math.Max(_peakPrivate, privateBytes);
            // GC committed vs. private memory: if private grows and this doesn't, it's native (WPF, Windows).
            GCMemoryInfo gc = GC.GetGCMemoryInfo();
            return $"CPU {percent:F2}% (max {_peakCpu:F2}%), memory {Mb(self.WorkingSet64)} MB in use, "
                + $"{Mb(privateBytes)} MB private (max {Mb(_peakPrivate)} MB), GC {Mb(GC.GetTotalMemory(false))} MB live / {Mb(gc.TotalCommittedBytes)} MB committed "
                + $"(collections {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}), threads {self.Threads.Count}, handles {self.HandleCount}";
        }
    }

    private static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("F1");
}

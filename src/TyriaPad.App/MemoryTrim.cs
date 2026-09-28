using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

using TyriaPad.Core.Diagnostics;

namespace TyriaPad.App;

/// <summary>
/// Gives memory back to Windows at the moments TyriaPad stops needing it: when startup finishes,
/// when the settings window closes (WPF and its theme stay loaded) and when going
/// idle. Compacts the GC and empties the working set; whatever is used again reloads on its own.
/// </summary>
internal static partial class MemoryTrim
{
    public static void Now(string reason)
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        using Process self = Process.GetCurrentProcess();
        bool trimmed = K32EmptyWorkingSet(self.Handle);
        Log.Write($"Memory trimmed ({reason}){(trimmed ? string.Empty : ": Windows did not allow it")}");
    }

    // Allocated since the last collection above which a collection runs (see CollectIfGrown).
    private const long CollectThresholdBytes = 12 * 1024 * 1024;

    /// <summary>
    /// Collects if garbage has piled up. In such a small program the GC takes very long to act on its own
    /// (its budget depends on the processor cache): on the Ally minutes went by without a
    /// collection, and along with the garbage stayed WPF's native rendering resources that
    /// hang from it, so private memory kept climbing. With a heap of a few MB it takes
    /// milliseconds. Called from the heartbeat (every 10 s).
    /// </summary>
    public static void CollectIfGrown()
    {
        if (GC.GetTotalMemory(forceFullCollection: false) > CollectThresholdBytes)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        }
    }

    /// <summary>Trims after <paramref name="delay"/>, once the UI thread is free.</summary>
    public static void After(TimeSpan delay, string reason)
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Now(reason);
        };
        timer.Start();
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool K32EmptyWorkingSet(nint process);
}

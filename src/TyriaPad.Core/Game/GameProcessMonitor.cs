using System.ComponentModel;
using System.Diagnostics;

using TyriaPad.Core.Diagnostics;

namespace TyriaPad.Core.Game;

/// <summary>
/// Knows whether GW2 is running without polling the process list: it attaches to the process when
/// its window comes to the foreground (and once at startup, in case it was already running) and waits
/// for it to exit. When it exits it looks again a few seconds later, because the GW2 launcher can
/// close and start another game process; only if none is left does it raise <see cref="Exited"/>.
/// </summary>
public sealed class GameProcessMonitor : IDisposable
{
    private static readonly TimeSpan RecheckDelay = TimeSpan.FromSeconds(10);

    private readonly FocusWatcher _focus;
    private readonly string _processName;
    private readonly Lock _lock = new();
    private readonly Timer _recheck;
    private Process? _process;
    private bool _disposed;

    public GameProcessMonitor(FocusWatcher focus, string processName = FocusWatcher.Gw2ProcessName)
    {
        _focus = focus;
        _processName = processName;
        _recheck = new Timer(_ => Recheck(), null, Timeout.Infinite, Timeout.Infinite);
        _focus.GameActivated += Attach;
        AttachToRunning();
    }

    /// <summary>Raised (from a pool thread) when GW2 closes and no other process of it is left.</summary>
    public event Action? Exited;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _process is not null;
            }
        }
    }

    /// <summary>Pid of the watched process, or null if GW2 is closed.</summary>
    public int? ProcessId
    {
        get
        {
            lock (_lock)
            {
                return _process?.Id;
            }
        }
    }

    public void Dispose()
    {
        _focus.GameActivated -= Attach;
        lock (_lock)
        {
            _disposed = true;
            Release();
        }

        _recheck.Dispose();
    }

    private void AttachToRunning()
    {
        Process[] running = Process.GetProcessesByName(_processName);
        try
        {
            if (running.Length > 0)
            {
                Attach(running[0].Id);
            }
        }
        finally
        {
            foreach (Process process in running)
            {
                process.Dispose();
            }
        }
    }

    private void Attach(int pid)
    {
        lock (_lock)
        {
            if (_disposed || _process?.Id == pid)
            {
                return;
            }

            Process process;
            try
            {
                process = Process.GetProcessById(pid);
                process.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // No permission to wait on the process (e.g. GW2 running as administrator): treated as
                // running until the next check.
                Log.Write($"GW2: cannot watch process {pid}: {ex.Message}");
                return;
            }

            Release();
            _process = process;
            process.Exited += OnExited;
            Log.Write($"GW2 running (pid {pid})");
            if (process.HasExited)
            {
                OnExited(process, EventArgs.Empty);
            }
        }
    }

    private void OnExited(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_disposed || sender is not Process process || !ReferenceEquals(process, _process))
            {
                return;
            }

            Log.Write($"GW2 exited (pid {process.Id}); checking in {RecheckDelay.TotalSeconds:0} s whether another process is left");
            Release();
        }

        _recheck.Change(RecheckDelay, Timeout.InfiniteTimeSpan);
    }

    private void Recheck()
    {
        AttachToRunning();
        if (!IsRunning && !_disposed)
        {
            Log.Write("GW2 closed");
            Exited?.Invoke();
        }
    }

    private void Release()
    {
        if (_process is { } process)
        {
            process.Exited -= OnExited;
            process.Dispose();
            _process = null;
        }
    }
}

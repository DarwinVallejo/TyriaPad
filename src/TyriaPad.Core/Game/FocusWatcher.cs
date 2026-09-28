using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

using TyriaPad.Core.Diagnostics;
using TyriaPad.Core.Native;

namespace TyriaPad.Core.Game;

/// <summary>
/// Knows whether the foreground window belongs to GW2. Listens to EVENT_SYSTEM_FOREGROUND and, as a
/// safety net, compares every 500 ms against GetForegroundWindow (a trivial call) in case the hook
/// doesn't fire, which can happen in shells like the Xbox full screen experience.
/// Must be created on a thread with a message loop (the UI thread), which is where the events arrive.
/// </summary>
public sealed class FocusWatcher : IDisposable
{
    public const string Gw2ProcessName = "Gw2-64";

    private static readonly TimeSpan VerifyInterval = TimeSpan.FromMilliseconds(500);

    private readonly string _processName;
    private readonly User32.WinEventProc _callback; // kept alive so the GC doesn't collect it
    private readonly nint _hook;
    private readonly Timer _verifyTimer;
    private readonly Lock _lock = new();
    private nint _lastHwnd;
    private uint _lastPid;
    private bool _lastPidIsGame;
    private string _lastPidName = "?";
    private string _foregroundName = "?";
    private volatile bool _isGameFocused;
    private nint _gameWindow;

    public FocusWatcher(string processName = Gw2ProcessName)
    {
        _processName = processName;
        _callback = OnWinEvent;
        _hook = User32.SetWinEventHook(
            User32.EventSystemForeground,
            User32.EventSystemForeground,
            0,
            Marshal.GetFunctionPointerForDelegate(_callback),
            0,
            0,
            User32.WinEventOutOfContext);
        if (_hook == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        Update(User32.GetForegroundWindow(), "startup");
        _verifyTimer = new Timer(_ => Verify(), null, VerifyInterval, VerifyInterval);
    }

    /// <summary>A GW2 window came to the foreground (process pid). Raised on the thread that detected the change.</summary>
    public event Action<int>? GameActivated;

    public bool IsGameFocused => _isGameFocused;

    /// <summary>Last GW2 window that was in the foreground (0 if none yet), to align the overlay.</summary>
    public nint GameWindow => _gameWindow;

    /// <summary>Name of the foreground process, for the log.</summary>
    public string ForegroundName
    {
        get
        {
            lock (_lock)
            {
                return _foregroundName;
            }
        }
    }

    public void Dispose()
    {
        _verifyTimer.Dispose();
        User32.UnhookWinEvent(_hook);
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint timeMs)
    {
        Update(hwnd, "hook");
    }

    private void Verify()
    {
        nint hwnd = User32.GetForegroundWindow();
        lock (_lock)
        {
            if (hwnd == _lastHwnd)
            {
                return;
            }
        }

        Update(hwnd, "check (the hook didn't fire)");
    }

    private void Update(nint hwnd, string source)
    {
        int activated = 0;
        lock (_lock)
        {
            if (hwnd == _lastHwnd && source != "startup")
            {
                return;
            }

            _lastHwnd = hwnd;
            User32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
            {
                _foregroundName = "(none)";
                _isGameFocused = false;
            }
            else
            {
                _isGameFocused = IsGameProcess(pid);
                if (_isGameFocused)
                {
                    _gameWindow = hwnd;
                    activated = (int)pid;
                }
            }

            Log.Write($"Foreground: {_foregroundName} (pid {pid}, window \"{GetClassName(hwnd)}\"){(_isGameFocused ? " = game" : "")} [{source}]");
        }

        if (activated != 0)
        {
            GameActivated?.Invoke(activated);
        }
    }

    private bool IsGameProcess(uint pid)
    {
        if (pid == _lastPid)
        {
            _foregroundName = _lastPidName;
            return _lastPidIsGame;
        }

        bool isGame;
        string name;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
            isGame = string.Equals(name, _processName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            name = $"? ({ex.GetType().Name})";
            isGame = false;
        }

        _lastPid = pid;
        _lastPidIsGame = isGame;
        _lastPidName = name;
        _foregroundName = name;
        return isGame;
    }

    private static unsafe string GetClassName(nint hwnd)
    {
        char* buffer = stackalloc char[256];
        int length = User32.GetClassName(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : "?";
    }
}
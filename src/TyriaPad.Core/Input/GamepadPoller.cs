using System.Diagnostics;

using TyriaPad.Core.Native;

namespace TyriaPad.Core.Input;

/// <summary>
/// Dedicated thread that reads the controller at ~250 Hz (GW2 focused), 10 Hz (GW2 open but not
/// focused) or 1 Hz (GW2 closed: idle, saves battery)
/// and delivers one <see cref="GamepadFrame"/> per cycle. It's delivered even if nothing changed
/// because the right stick moves the mouse continuously.
/// </summary>
public sealed class GamepadPoller : IDisposable
{
    public static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(4);
    public static readonly TimeSpan SlowInterval = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan DormantInterval = TimeSpan.FromSeconds(1);

    // Avoids mouse jumps when going from the slow rate to the fast one.
    private static readonly TimeSpan MaxDelta = TimeSpan.FromMilliseconds(50);

    private readonly IGamepadSource _source;
    private volatile InputSettings _settings;
    private readonly Func<PollRate> _rate;
    private readonly Action<GamepadFrame> _onFrame;
    private readonly Thread _thread;
    private volatile bool _stopping;

    public GamepadPoller(IGamepadSource source, InputSettings settings, Func<PollRate> rate, Action<GamepadFrame> onFrame)
    {
        _source = source;
        _settings = settings;
        _rate = rate;
        _onFrame = onFrame;
        _thread = new Thread(Run)
        {
            Name = "TyriaPad.GamepadPoller",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
    }

    /// <summary>Deadzones and thresholds. Can be changed on the fly; applies on the next cycle.</summary>
    public InputSettings Settings
    {
        get => _settings;
        set => _settings = value;
    }

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _stopping = true;
        if (_thread.IsAlive)
        {
            _thread.Join();
        }
    }

    private void Run()
    {
        using var timer = new HighResolutionTimer();
        long start = Stopwatch.GetTimestamp();
        TimeSpan last = TimeSpan.Zero;
        GamepadButtons previous = GamepadButtons.None;

        while (!_stopping)
        {
            bool connected = _source.TryRead(out RawGamepad raw);
            GamepadState state = connected ? GamepadState.FromRaw(raw, previous, _settings) : default;

            TimeSpan now = Stopwatch.GetElapsedTime(start);
            TimeSpan delta = now - last;
            if (delta > MaxDelta)
            {
                delta = MaxDelta;
            }

            last = now;
            _onFrame(GamepadFrame.Create(previous, state, connected, now, delta));
            previous = state.Buttons;

            timer.Sleep(_rate() switch
            {
                PollRate.Fast => FastInterval,
                PollRate.Slow => SlowInterval,
                _ => DormantInterval,
            });
        }
    }
}
/// <summary>Controller read rate.</summary>
public enum PollRate
{
    /// <summary>GW2 in the foreground: every 4 ms.</summary>
    Fast,

    /// <summary>GW2 open but not focused: every 100 ms, to come back right away on Alt+Tab.</summary>
    Slow,

    /// <summary>GW2 closed: once a second, only to know whether a controller is there.</summary>
    Dormant,
}

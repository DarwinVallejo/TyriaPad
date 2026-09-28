using TyriaPad.Core.Config;
using TyriaPad.Core.Diagnostics;
using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;
using TyriaPad.Core.Overlay;

namespace TyriaPad.Core;

public enum EngineState
{
    Paused,
    NoController,
    WaitingForGame,
    TextboxFocused,
    Active,
}

public readonly record struct EngineStatus(EngineState State, CameraMode Mode, GameContext Context, string ProfileName);

/// <summary>
/// Ties together poller, mapping and output. Only emits input with GW2 in the foreground, not paused
/// and with no text box active; in any other case it releases everything and the controller behaves normally.
/// The configuration is changed on the fly with <see cref="Apply"/>.
/// </summary>
public sealed class TyriaPadEngine : IDisposable
{
    private readonly IInputSink _sink;
    private readonly Func<bool> _isGameFocused;
    private readonly Func<bool> _isTextboxFocused;
    private readonly Func<bool?> _isCursorVisible;
    private readonly Func<GameSignals> _gameSignals;
    private readonly Func<bool> _isGameRunning;
    private readonly GamepadPoller _poller;
    private ProfileMapper _mapper;
    private LoadedConfig _config;
    private LoadedConfig? _pendingConfig;
    private volatile bool _paused;
    private bool _active;
    private bool? _lastCursorVisible;
    private GamepadFrame _lastFrame;
    private EngineStatus _status;
    private OverlayState _overlay;

    /// <param name="isCursorVisible">Windows cursor visible; null = unknown (doesn't change the mode).</param>
    /// <param name="gameSignals">Mount and map from MumbleLink.</param>
    /// <param name="isGameRunning">GW2 running; if not, the controller is read at the idle rate.</param>
    public TyriaPadEngine(
        IGamepadSource source,
        IInputSink sink,
        Func<bool> isGameFocused,
        Func<bool> isTextboxFocused,
        Func<bool?> isCursorVisible,
        Func<GameSignals> gameSignals,
        LoadedConfig config,
        Func<bool>? isGameRunning = null)
    {
        _sink = sink;
        _isGameFocused = isGameFocused;
        _isTextboxFocused = isTextboxFocused;
        _isCursorVisible = isCursorVisible;
        _gameSignals = gameSignals;
        _isGameRunning = isGameRunning ?? (static () => true);
        _config = config;
        _mapper = new ProfileMapper(new InputEmitter(sink), config.Settings, config.Profile);
        _poller = new GamepadPoller(source, config.Settings.Input, CurrentPollRate, OnFrame);
        _status = MakeStatus(EngineState.NoController);
        _overlay = MakeOverlay(EngineState.NoController);
    }

    /// <summary>Raised on the poller thread when the state, mode or context changes.</summary>
    public event Action<EngineStatus>? StatusChanged;

    /// <summary>Raised on the poller thread when something the overlay draws changes (state, layer, context, radial).</summary>
    public event Action<OverlayState>? OverlayChanged;

    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    public EngineStatus Status => _status;

    /// <summary>Last state published to the overlay.</summary>
    public OverlayState Overlay => _overlay;

    public void Start() => _poller.Start();

    /// <summary>Applies another configuration. Takes effect on the next poller cycle, releasing everything first.</summary>
    public void Apply(LoadedConfig config) => Volatile.Write(ref _pendingConfig, config);

    public void Dispose()
    {
        _poller.Dispose();
        _mapper.Deactivate();
    }

    private PollRate CurrentPollRate()
        => !_paused && _isGameFocused() ? PollRate.Fast
        : _isGameRunning() ? PollRate.Slow
        : PollRate.Dormant;

    /// <summary>Last state read from the controller, for the log heartbeat.</summary>
    public string DescribeInput()
    {
        GamepadState s = _lastFrame.State;
        return $"buttons [{s.Buttons}], left stick ({s.Left.X:F2}, {s.Left.Y:F2}), right ({s.Right.X:F2}, {s.Right.Y:F2}), "
            + $"LT {s.LeftTrigger:F2}, RT {s.RightTrigger:F2}";
    }

    private void OnFrame(GamepadFrame frame)
    {
        _lastFrame = frame;
        if ((frame.Pressed | frame.Released) != 0)
        {
            // Always logged, active or not: shows whether Windows delivers the controller to TyriaPad.
            Log.Write($"Controller: [{frame.State.Buttons}]");
        }

        EngineState state;
        try
        {
            SwapConfigIfPending(frame);

            state = Evaluate(frame);
            bool active = state == EngineState.Active;
            if (active != _active)
            {
                if (active)
                {
                    _mapper.Activate(frame);
                }
                else
                {
                    _mapper.Deactivate();
                }

                _active = active;
            }

            if (active)
            {
                // In the Xbox full screen experience Windows hides the cursor outside GW2, but inside
                // the game it's still reliable (verified on the Ally X), so it's followed the same way.
                if (_isCursorVisible() is bool cursorVisible)
                {
                    if (cursorVisible != _lastCursorVisible)
                    {
                        _lastCursorVisible = cursorVisible;
                        Log.Write($"Windows cursor: {(cursorVisible ? "visible" : "hidden")}");
                    }

                    _mapper.ObserveCursor(cursorVisible, frame.Time);
                }

                _mapper.ObserveGame(_gameSignals());
                _mapper.Process(frame);
            }
        }
        catch (Exception ex)
        {
            // On any failure, better to release everything than leave a key stuck.
            Log.Write($"Error processing the controller: {ex}");
            _mapper.Deactivate();
            _active = false;
            return;
        }

        EngineStatus status = MakeStatus(state);
        if (status != _status)
        {
            _status = status;
            Log.Write($"State: {status.State}, mode {status.Mode}, context {status.Context}");
            StatusChanged?.Invoke(status);
        }

        OverlayState overlay = MakeOverlay(state);
        if (overlay != _overlay)
        {
            _overlay = overlay;
            OverlayChanged?.Invoke(overlay);
        }
    }

    private OverlayState MakeOverlay(EngineState state)
        => new(state, _mapper.Mode, _mapper.Context, _mapper.ActiveLayout, _mapper.ActiveLayer, _mapper.Radial);

    private void SwapConfigIfPending(in GamepadFrame frame)
    {
        LoadedConfig? pending = Interlocked.Exchange(ref _pendingConfig, null);
        if (pending is null)
        {
            return;
        }

        _mapper.Deactivate();
        _config = pending;
        _mapper = new ProfileMapper(new InputEmitter(_sink), pending.Settings, pending.Profile, _mapper.Mode);
        _poller.Settings = pending.Settings.Input;
        if (_active)
        {
            _mapper.Activate(frame);
        }

        Log.Write($"Configuration applied: profile \"{pending.Profile.Name}\"");
    }

    private EngineStatus MakeStatus(EngineState state) => new(state, _mapper.Mode, _mapper.Context, _config.Profile.Name);

    private EngineState Evaluate(in GamepadFrame frame)
    {
        if (_paused)
        {
            return EngineState.Paused;
        }

        if (!frame.Connected)
        {
            return EngineState.NoController;
        }

        if (!_isGameFocused())
        {
            return EngineState.WaitingForGame;
        }

        return _isTextboxFocused() ? EngineState.TextboxFocused : EngineState.Active;
    }
}

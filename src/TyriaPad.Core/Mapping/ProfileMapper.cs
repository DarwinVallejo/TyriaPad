using TyriaPad.Core.Config;
using TyriaPad.Core.Diagnostics;
using TyriaPad.Core.Input;
using TyriaPad.Core.Output;

namespace TyriaPad.Core.Mapping;

public enum CameraMode
{
    /// <summary>GW2 Action Camera: the right stick turns the camera and you aim with the reticle.</summary>
    ActionCamera,

    /// <summary>Free cursor for menus, vendors and inventory.</summary>
    Pointer,
}

/// <summary>What MumbleLink says about the game that changes the context.</summary>
public readonly record struct GameSignals(byte MountIndex, bool MapOpen);

/// <summary>
/// Applies a <see cref="Profile"/>: picks the context (combat / cursor / mount), the layer from the
/// held modifiers and each button's binding, detects gestures and emits the output. The
/// camera/cursor mode follows the Windows cursor and is forced with the <c>actionCamera</c> action.
/// Used only from the poller thread.
/// </summary>
public sealed class ProfileMapper
{
    private static readonly GamepadButtons[] AllButtons =
        [.. Enum.GetValues<GamepadButtons>().Where(static b => b != GamepadButtons.None)];

    private readonly InputEmitter _emitter;
    private readonly TyriaPadSettings _settings;
    private readonly Key _actionCameraKey;
    private readonly GestureDetector _gestures;
    private readonly Dictionary<GameContext, LayerEngine> _engines = [];
    private readonly Dictionary<GamepadButtons, ButtonBinding> _pressBindings = [];
    private readonly Dictionary<GamepadButtons, ActiveAction> _active = [];
    private readonly Dictionary<GamepadButtons, ModifierPress> _modifierPresses = [];
    private readonly Dictionary<GamepadButtons, TimeSpan> _pressedAt = [];
    private readonly StickToMouse _leftMouse = new();
    private readonly StickToMouse _rightMouse = new();
    private LayerEngine _layers;
    private GameContext _context;
    private GamepadButtons _down;
    private GamepadButtons _ignored;
    private RadialSession? _radial;
    private GamepadButtons _radialButton;
    private bool _moveUp;
    private bool _moveDown;
    private bool _moveLeft;
    private bool _moveRight;
    private CameraMode? _cursorMode;
    private TimeSpan _cursorModeSince;
    private TimeSpan _manualUntil;
    private GameSignals _signals;
    private TimeSpan _now;

    /// <param name="initialMode">GW2 starts without Action Camera, so by default it starts in cursor mode.</param>
    public ProfileMapper(InputEmitter emitter, TyriaPadSettings settings, Profile profile, CameraMode initialMode = CameraMode.Pointer)
    {
        _emitter = emitter;
        _settings = settings;
        _actionCameraKey = settings.ActionCameraKeyCode;
        Profile = profile;
        Mode = initialMode;
        _gestures = new GestureDetector(settings.Gestures, OnGesture);
        _context = ResolveContext();
        _layers = GetEngine(_context);
    }

    public Profile Profile { get; }

    public CameraMode Mode { get; private set; }

    /// <summary>Active context. If the profile doesn't define the requested one, the combat one is used.</summary>
    public GameContext Context => _layers.Layout.Context;

    public Layer ActiveLayer => _layers.Current;

    /// <summary>Layers of the active context, for the overlay.</summary>
    public ContextLayout ActiveLayout => _layers.Layout;

    /// <summary>Radial open right now, for the overlay.</summary>
    public RadialState? Radial => _radial?.State;

    /// <summary>
    /// Starts emitting. Buttons that were already pressed are ignored until they are released,
    /// so nothing the user pressed outside the game gets sent.
    /// </summary>
    public void Activate(in GamepadFrame frame)
    {
        _ignored = frame.State.Buttons;
        _down = GamepadButtons.None;
    }

    /// <summary>Stops emitting and releases everything that was pressed.</summary>
    public void Deactivate()
    {
        _emitter.ReleaseAll();
        _active.Clear();
        _pressBindings.Clear();
        _modifierPresses.Clear();
        _pressedAt.Clear();
        _gestures.Reset();
        _radial = null;
        _moveUp = _moveDown = _moveLeft = _moveRight = false;
        _down = GamepadButtons.None;
        _ignored = GamepadButtons.None;
        _leftMouse.Reset();
        _rightMouse.Reset();
        _layers.Reset();
    }

    /// <summary>
    /// Reports the Windows cursor. If <c>followCursor</c> is on and the state holds,
    /// the mode switches to cursor (visible) or camera (hidden).
    /// </summary>
    public void ObserveCursor(bool visible, TimeSpan now)
    {
        CameraMode cursorMode = visible ? CameraMode.Pointer : CameraMode.ActionCamera;
        if (cursorMode != _cursorMode)
        {
            _cursorMode = cursorMode;
            _cursorModeSince = now;
        }

        if (_settings.FollowCursor
            && now >= _manualUntil
            && now - _cursorModeSince >= _settings.CursorDebounce
            && Mode != cursorMode)
        {
            SetMode(cursorMode);
        }
    }

    public void ObserveGame(GameSignals signals) => _signals = signals;

    public void Process(in GamepadFrame frame)
    {
        _now = frame.Time;
        _emitter.Update(_now);
        UpdateContext();

        foreach (GamepadButtons button in AllButtons)
        {
            if ((frame.Released & button) != 0)
            {
                OnReleased(button);
            }
            else if ((frame.Pressed & button) != 0 && (_ignored & button) == 0)
            {
                OnPressed(button);
            }
        }

        _gestures.Update(_now);
        UpdateActive();
        _radial?.Update(_radial.Menu.Stick == StickSide.Left ? frame.State.Left : frame.State.Right);
        UpdateSticks(frame);
    }

    /// <summary>Summary for the log: context and active layer.</summary>
    public string Describe() => $"{Context}/{_layers.Current.Name}";

    private GameContext ResolveContext()
    {
        if (Mode == CameraMode.Pointer || (_settings.PointerWhenMapOpen && _signals.MapOpen))
        {
            return GameContext.Pointer;
        }

        return _signals.MountIndex != 0 ? GameContext.Mount : GameContext.Combat;
    }

    private LayerEngine GetEngine(GameContext context)
    {
        if (!_engines.TryGetValue(context, out LayerEngine? engine))
        {
            engine = new LayerEngine(Profile.Resolve(context));
            _engines[context] = engine;
        }

        return engine;
    }

    private void UpdateContext()
    {
        GameContext wanted = ResolveContext();
        if (wanted == _context)
        {
            return;
        }

        _context = wanted;
        _layers = GetEngine(wanted);
        _layers.Update(_down);
        Log.Write($"Context: {Describe()}");
    }

    private void OnPressed(GamepadButtons button)
    {
        GamepadButtons heldBefore = _down;
        _down |= button;
        _pressedAt[button] = _now;
        foreach (ModifierPress press in _modifierPresses.Values)
        {
            press.Used = true;
        }

        if (_layers.IsModifierPress(button, heldBefore))
        {
            // A modifier released right away without being used does its tap (e.g. LB in cursor = right click).
            ButtonBinding? own = _layers.Current.Find(button);
            _modifierPresses[button] = new ModifierPress(_now, own?.Tap ?? own?.Press);
            _layers.Update(_down);
            Log.Write($"Layer: {Describe()}");
            return;
        }

        ButtonBinding? binding = _layers.Current.Find(button);
        if (binding is null)
        {
            // An old profile next to the executable (without M1/M2, for example) shows up here.
            Log.Write($"{KeyNames.ButtonName(button)} has no action [{Describe()}]");
            return;
        }

        _pressBindings[button] = binding;
        if (binding.Press is not null)
        {
            Begin(binding.Press, button, "press");
        }
        else
        {
            _gestures.Press(button, _now, binding.Hold is not null, binding.Double is not null);
        }
    }

    private void OnReleased(GamepadButtons button)
    {
        _down &= ~button;
        _ignored &= ~button;
        _pressedAt.Remove(button);

        if (_modifierPresses.Remove(button, out ModifierPress? modifier))
        {
            if (!modifier.Used && modifier.Tap is not null && _now - modifier.At < _settings.Gestures.Hold)
            {
                Fire(modifier.Tap, button, "tap");
            }
        }
        else if (_pressBindings.TryGetValue(button, out ButtonBinding? binding))
        {
            if (binding.Press is not null)
            {
                End(button);
            }
            else
            {
                _gestures.Release(button, _now);
            }
        }

        if (_layers.Update(_down))
        {
            Log.Write($"Layer: {Describe()}");
        }
    }

    private void OnGesture(GamepadButtons button, Gesture gesture)
    {
        if (!_pressBindings.TryGetValue(button, out ButtonBinding? binding))
        {
            return;
        }

        switch (gesture)
        {
            case Gesture.Tap:
                Fire(binding.Tap, button, "tap");
                break;
            case Gesture.HoldStart:
                Begin(binding.Hold, button, "hold");
                break;
            case Gesture.HoldEnd:
                End(button);
                break;
            case Gesture.DoubleTap:
                Fire(binding.Double, button, "2x");
                break;
        }
    }

    /// <summary>Starts an action that lasts as long as the button (or the hold) does.</summary>
    private void Begin(BindingAction? action, GamepadButtons button, string gesture)
    {
        if (action is null)
        {
            return;
        }

        Trace(button, action, gesture);
        switch (action)
        {
            case ChordAction chord:
                _emitter.Press(chord.Chord);
                _active[button] = new ActiveAction(action);
                break;
            case WheelAction wheel:
                _emitter.Wheel(wheel.Direction);
                _active[button] = new ActiveAction(action) { NextRepeatAt = _now + _settings.Gestures.WheelRepeat };
                break;
            case ActionCameraToggleAction:
                ToggleActionCamera();
                _active[button] = new ActiveAction(action);
                break;
            case SetModeAction mode:
                ApplyMode(mode);
                break;
            case RadialAction radial:
                OpenRadial(radial.Name, button);
                _active[button] = new ActiveAction(action);
                break;
        }
    }

    private void End(GamepadButtons button)
    {
        if (!_active.Remove(button, out ActiveAction? active))
        {
            return;
        }

        switch (active.Action)
        {
            case ChordAction chord:
                _emitter.Release(chord.Chord);
                break;
            case RadialAction:
                CloseRadial(choose: true);
                break;
        }
    }

    /// <summary>Runs an action once (tap, double tap, radial option).</summary>
    private void Fire(BindingAction? action, GamepadButtons button, string gesture)
    {
        if (action is null)
        {
            return;
        }

        Trace(button, action, gesture);
        switch (action)
        {
            case ChordAction chord:
                _emitter.Tap(chord.Chord, _now);
                break;
            case WheelAction wheel:
                _emitter.Wheel(wheel.Direction);
                break;
            case ActionCameraToggleAction:
                ToggleActionCamera();
                break;
            case SetModeAction mode:
                ApplyMode(mode);
                break;
            case RadialAction:
                Log.Write("A radial only opens by holding the button");
                break;
        }
    }

    private void UpdateActive()
    {
        foreach ((GamepadButtons button, ActiveAction active) in _active)
        {
            switch (active.Action)
            {
                case WheelAction wheel when _now >= active.NextRepeatAt:
                    _emitter.Wheel(wheel.Direction);
                    active.NextRepeatAt = _now + _settings.Gestures.WheelRepeat;
                    break;
                case ActionCameraToggleAction when !active.Resynced
                    && _pressedAt.TryGetValue(button, out TimeSpan pressedAt)
                    && _now - pressedAt >= _settings.Gestures.Resync:
                    // The game already got the key; here only the internal state is corrected.
                    active.Resynced = true;
                    ToggleMode();
                    Log.Write($"Mode resynced to {Mode}");
                    break;
            }
        }
    }

    private void Trace(GamepadButtons button, BindingAction action, string gesture)
        => Log.Write($"{KeyNames.ButtonName(button)} {gesture} -> {action} [{Describe()}]");

    private void OpenRadial(string name, GamepadButtons button)
    {
        CloseRadial(choose: false);
        _radial = new RadialSession(Profile.Radials[name]);
        _radialButton = button;
    }

    private void CloseRadial(bool choose)
    {
        if (_radial is null)
        {
            return;
        }

        RadialSession radial = _radial;
        _radial = null;
        if (choose && radial.Selected is { } index)
        {
            RadialItem item = radial.Menu.Items[index];
            Fire(item.Action, _radialButton, $"radial {radial.Menu.Name}: {item.Label}");
        }
    }

    private void ToggleActionCamera()
    {
        _emitter.Tap(_actionCameraKey, _now);
        ToggleMode();
    }

    private void ApplyMode(SetModeAction action)
    {
        _manualUntil = _now + _settings.ManualGrace;
        SetMode(action.Mode ?? Opposite(Mode));
    }

    private void ToggleMode()
    {
        // Gives the game time to show or hide the cursor before following it again.
        _manualUntil = _now + _settings.ManualGrace;
        SetMode(Opposite(Mode));
    }

    private static CameraMode Opposite(CameraMode mode)
        => mode == CameraMode.ActionCamera ? CameraMode.Pointer : CameraMode.ActionCamera;

    private void SetMode(CameraMode mode)
    {
        Mode = mode;
        _leftMouse.Reset();
        _rightMouse.Reset();
    }

    private void UpdateSticks(in GamepadFrame frame)
    {
        StickRole left = _radial?.Menu.Stick == StickSide.Left ? StickRole.None : _layers.LeftStick;
        StickRole right = _radial?.Menu.Stick == StickSide.Right ? StickRole.None : _layers.RightStick;

        Stick moveStick = left == StickRole.Move ? frame.State.Left
            : right == StickRole.Move ? frame.State.Right
            : default;
        UpdateMovement(moveStick);

        (int dx, int dy) = MouseDelta(_leftMouse, left, frame.State.Left, frame.Delta);
        (int dx2, int dy2) = MouseDelta(_rightMouse, right, frame.State.Right, frame.Delta);
        _emitter.MoveMouse(dx + dx2, dy + dy2);
    }

    private (int Dx, int Dy) MouseDelta(StickToMouse mouse, StickRole role, Stick stick, TimeSpan delta)
    {
        switch (role)
        {
            case StickRole.Camera:
                return mouse.Update(stick, _settings.Camera, delta);
            case StickRole.Pointer:
                return mouse.Update(stick, _settings.Pointer, delta);
            default:
                mouse.Reset();
                return (0, 0);
        }
    }

    private void UpdateMovement(Stick stick)
    {
        UpdateDirection(ref _moveUp, stick.Y, Key.W);
        UpdateDirection(ref _moveDown, -stick.Y, Key.S);
        UpdateDirection(ref _moveRight, stick.X, Key.D);
        UpdateDirection(ref _moveLeft, -stick.X, Key.A);
    }

    private void UpdateDirection(ref bool held, float value, Key key)
    {
        bool shouldHold = held
            ? value > _settings.Movement.ReleaseThreshold
            : value >= _settings.Movement.PressThreshold;
        if (shouldHold == held)
        {
            return;
        }

        held = shouldHold;
        if (shouldHold)
        {
            _emitter.Press(key);
        }
        else
        {
            _emitter.Release(key);
        }
    }

    private sealed class ActiveAction(BindingAction action)
    {
        public BindingAction Action { get; } = action;

        public TimeSpan NextRepeatAt { get; set; }

        public bool Resynced { get; set; }
    }

    private sealed class ModifierPress(TimeSpan at, BindingAction? tap)
    {
        public TimeSpan At { get; } = at;

        public BindingAction? Tap { get; } = tap;

        public bool Used { get; set; }
    }
}

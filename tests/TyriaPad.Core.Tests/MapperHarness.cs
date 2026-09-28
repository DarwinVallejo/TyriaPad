using TyriaPad.Core.Config;
using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;

namespace TyriaPad.Core.Tests;

/// <summary>Simulates poller frames against a <see cref="ProfileMapper"/> and records what is emitted.</summary>
internal sealed class MapperHarness
{
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(4);

    private GamepadButtons _previous;

    public MapperHarness(Profile? profile = null, TyriaPadSettings? settings = null, CameraMode initialMode = CameraMode.ActionCamera)
    {
        Settings = settings ?? TyriaPadSettings.Default;
        Profile = profile ?? Defaults.Load().Profile;
        Mapper = new ProfileMapper(new InputEmitter(Sink), Settings, Profile, initialMode);
        Mapper.Activate(MakeFrame(GamepadButtons.None, default, default, TimeSpan.Zero));
    }

    public RecordingSink Sink { get; } = new();

    public ProfileMapper Mapper { get; }

    public Profile Profile { get; }

    public TyriaPadSettings Settings { get; }

    public TimeSpan Time { get; private set; }

    public List<string> Events => Sink.Events;

    public void Step(GamepadButtons buttons, Stick left = default, Stick right = default)
    {
        Time += Tick;
        Mapper.Process(MakeFrame(buttons, left, right, Time));
        _previous = buttons;
    }

    public void Hold(GamepadButtons buttons, TimeSpan duration, Stick left = default, Stick right = default)
    {
        for (TimeSpan elapsed = TimeSpan.Zero; elapsed < duration; elapsed += Tick)
        {
            Step(buttons, left, right);
        }
    }

    /// <summary>Presses and releases right away, and lets the tap duration pass so the key gets released.</summary>
    public void Tap(GamepadButtons buttons)
    {
        Step(buttons);
        Step(GamepadButtons.None);
        Hold(GamepadButtons.None, InputEmitter.TapDuration + Tick);
    }

    public void ObserveCursor(bool visible, TimeSpan duration)
    {
        for (TimeSpan elapsed = TimeSpan.Zero; elapsed < duration; elapsed += Tick)
        {
            Time += Tick;
            Mapper.ObserveCursor(visible, Time);
            Mapper.Process(MakeFrame(_previous, default, default, Time));
        }
    }

    public void EnterPointerMode()
    {
        Hold(GamepadButtons.Menu, TimeSpan.FromMilliseconds(300));
        Step(GamepadButtons.None);
        Hold(GamepadButtons.None, InputEmitter.TapDuration + Tick);
        Assert.Equal(CameraMode.Pointer, Mapper.Mode);
        Events.Clear();
    }

    public void Reactivate(GamepadButtons alreadyHeld)
    {
        Mapper.Deactivate();
        _previous = alreadyHeld;
        Mapper.Activate(MakeFrame(alreadyHeld, default, default, Time));
    }

    private GamepadFrame MakeFrame(GamepadButtons buttons, Stick left, Stick right, TimeSpan time)
    {
        var state = new GamepadState(buttons, left, right, 0f, 0f);
        return GamepadFrame.Create(_previous, state, true, time, Tick);
    }
}

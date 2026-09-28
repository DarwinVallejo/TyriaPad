using TyriaPad.Core.Output;

namespace TyriaPad.Core.Mapping;

/// <summary>What a button does. Written as text in the profile; see <see cref="ActionNames"/>.</summary>
public abstract record BindingAction;

/// <summary>Key or click, with optional modifiers. Held for as long as the gesture lasts.</summary>
public sealed record ChordAction(Chord Chord) : BindingAction
{
    public override string ToString() => Chord.ToString();
}

/// <summary>Mouse wheel. While held, it repeats every <c>gestures.wheelRepeatMs</c>.</summary>
/// <param name="Direction">+1 up, -1 down.</param>
public sealed record WheelAction(int Direction) : BindingAction
{
    public override string ToString() => Direction > 0 ? "wheel:up" : "wheel:down";
}

/// <summary>
/// Sends GW2's Action Camera key and toggles the internal mode (camera ↔ cursor).
/// If the button is still held at <c>gestures.resyncMs</c>, it flips only the internal mode back,
/// without sending a key, to resync with the game.
/// </summary>
public sealed record ActionCameraToggleAction : BindingAction
{
    public static readonly ActionCameraToggleAction Instance = new();

    public override string ToString() => ActionNames.ActionCamera;
}

/// <summary>Changes only the internal mode, without sending anything to the game.</summary>
/// <param name="Mode">null = toggle.</param>
public sealed record SetModeAction(CameraMode? Mode) : BindingAction
{
    public override string ToString() => "mode:" + (Mode switch
    {
        CameraMode.Pointer => "pointer",
        CameraMode.ActionCamera => "camera",
        _ => "toggle",
    });
}

/// <summary>Opens a profile radial menu while held; on release it emits the chosen option.</summary>
public sealed record RadialAction(string Name) : BindingAction
{
    public override string ToString() => "radial:" + Name;
}

using TyriaPad.Core.Input;

namespace TyriaPad.Core.Mapping;

/// <summary>Game situation that decides which set of layers is active.</summary>
public enum GameContext
{
    /// <summary>Default: Action Camera, skills.</summary>
    Combat,

    /// <summary>Cursor visible: menus, map, vendors. The buttons do clicks.</summary>
    Pointer,

    /// <summary>MumbleLink reports an active mount.</summary>
    Mount,
}

/// <summary>What a stick does in a layer.</summary>
public enum StickRole
{
    None,

    /// <summary>Movement keys (WASD).</summary>
    Move,

    /// <summary>Relative mouse with the camera sensitivity.</summary>
    Camera,

    /// <summary>Relative mouse with the cursor sensitivity.</summary>
    Pointer,
}

public enum StickSide
{
    Left,
    Right,
}

/// <summary>
/// Gestures of a button. If there is only <see cref="Press"/>, the action is held while the button is
/// pressed and there is no delay. With tap/hold/double, gestures are detected: the tap is emitted on release
/// (or, if there is a double tap, when the window expires) and the hold when the threshold is exceeded.
/// </summary>
public sealed record ButtonBinding(
    BindingAction? Press = null,
    BindingAction? Tap = null,
    BindingAction? Hold = null,
    BindingAction? Double = null)
{
    public bool UsesGestures => Press is null;

    public bool IsEmpty => Press is null && Tap is null && Hold is null && Double is null;

    public override string ToString()
    {
        if (Press is not null)
        {
            return Press.ToString()!;
        }

        var parts = new List<string>(3);
        if (Tap is not null)
        {
            parts.Add("tap " + Tap);
        }

        if (Hold is not null)
        {
            parts.Add("hold " + Hold);
        }

        if (Double is not null)
        {
            parts.Add("2x " + Double);
        }

        return string.Join(", ", parts);
    }
}

/// <summary>Layer of a context: active when its modifiers are held (none = base layer).</summary>
public sealed record Layer(
    string Name,
    GamepadButtons Modifiers,
    StickRole? LeftStick,
    StickRole? RightStick,
    IReadOnlyDictionary<GamepadButtons, ButtonBinding> Bindings)
{
    public int ModifierCount => System.Numerics.BitOperations.PopCount((uint)Modifiers);

    public ButtonBinding? Find(GamepadButtons button) => Bindings.GetValueOrDefault(button);
}

/// <summary>Layers and stick roles of a context. The layers are in profile order.</summary>
public sealed record ContextLayout(
    GameContext Context,
    StickRole LeftStick,
    StickRole RightStick,
    IReadOnlyList<Layer> Layers)
{
    public Layer Base => Layers.First(static l => l.Modifiers == GamepadButtons.None);
}

public sealed record RadialItem(string Label, BindingAction Action);

/// <summary>Radial menu: the options are laid out in a circle starting at the top and going clockwise.</summary>
public sealed record RadialMenu(string Name, StickSide Stick, IReadOnlyList<RadialItem> Items);

/// <summary>Profile already validated and with the inheritance between contexts resolved.</summary>
public sealed record Profile(
    string Name,
    IReadOnlyDictionary<GameContext, ContextLayout> Contexts,
    IReadOnlyDictionary<string, RadialMenu> Radials)
{
    /// <summary>Requested context or, if the profile doesn't define it, the combat one.</summary>
    public ContextLayout Resolve(GameContext context)
        => Contexts.GetValueOrDefault(context) ?? Contexts[GameContext.Combat];

    public bool Has(GameContext context) => Contexts.ContainsKey(context);
}

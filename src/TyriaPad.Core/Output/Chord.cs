namespace TyriaPad.Core.Output;

/// <summary>Modifiers of a chord. The mask matches the <c>mod</c> attribute of the GW2 keybinds XML.</summary>
[Flags]
public enum KeyModifiers : byte
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

/// <summary>A key or a click, optionally with modifiers (e.g. Shift+R or Alt+click). It's what a binding emits.</summary>
public readonly record struct Chord(KeyModifiers Modifiers, OutputAction Action)
{
    public static readonly Key[] ModifierOrder = [Key.LeftShift, Key.LeftCtrl, Key.LeftAlt];

    public Chord(OutputAction action)
        : this(KeyModifiers.None, action)
    {
    }

    public bool IsNone => Action.IsNone;

    public static implicit operator Chord(Key key) => new(OutputAction.FromKey(key));

    public static implicit operator Chord(MouseButton button) => new(OutputAction.FromMouse(button));

    /// <summary>Modifier keys in the order they're pressed.</summary>
    public IEnumerable<Key> ModifierKeys
    {
        get
        {
            if ((Modifiers & KeyModifiers.Shift) != 0)
            {
                yield return Key.LeftShift;
            }

            if ((Modifiers & KeyModifiers.Ctrl) != 0)
            {
                yield return Key.LeftCtrl;
            }

            if ((Modifiers & KeyModifiers.Alt) != 0)
            {
                yield return Key.LeftAlt;
            }
        }
    }

    public override string ToString()
    {
        if (Modifiers == KeyModifiers.None)
        {
            return Action.ToString();
        }

        var parts = new List<string>(4);
        if ((Modifiers & KeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((Modifiers & KeyModifiers.Ctrl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((Modifiers & KeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        parts.Add(Action.ToString());
        return string.Join('+', parts);
    }
}

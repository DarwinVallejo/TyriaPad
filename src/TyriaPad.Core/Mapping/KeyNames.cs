using TyriaPad.Core.Input;
using TyriaPad.Core.Output;

namespace TyriaPad.Core.Mapping;

/// <summary>Key and button names as written in the JSON profiles (case-insensitive).</summary>
public static class KeyNames
{
    private static readonly Dictionary<string, Key> s_keys = BuildKeys();
    private static readonly Dictionary<string, GamepadButtons> s_buttons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = GamepadButtons.A,
        ["B"] = GamepadButtons.B,
        ["X"] = GamepadButtons.X,
        ["Y"] = GamepadButtons.Y,
        ["LB"] = GamepadButtons.LeftBumper,
        ["RB"] = GamepadButtons.RightBumper,
        ["LT"] = GamepadButtons.LeftTrigger,
        ["RT"] = GamepadButtons.RightTrigger,
        ["L3"] = GamepadButtons.LeftStick,
        ["R3"] = GamepadButtons.RightStick,
        ["LS"] = GamepadButtons.LeftStick,
        ["RS"] = GamepadButtons.RightStick,
        ["Menu"] = GamepadButtons.Menu,
        ["Start"] = GamepadButtons.Menu,
        ["View"] = GamepadButtons.View,
        ["Select"] = GamepadButtons.View,
        ["Back"] = GamepadButtons.View,
        ["Up"] = GamepadButtons.DPadUp,
        ["Down"] = GamepadButtons.DPadDown,
        ["Left"] = GamepadButtons.DPadLeft,
        ["Right"] = GamepadButtons.DPadRight,
        ["DPadUp"] = GamepadButtons.DPadUp,
        ["DPadDown"] = GamepadButtons.DPadDown,
        ["DPadLeft"] = GamepadButtons.DPadLeft,
        ["DPadRight"] = GamepadButtons.DPadRight,
        ["M1"] = GamepadButtons.M1,
        ["M2"] = GamepadButtons.M2,
    };

    public static bool TryParseKey(string name, out Key key) => s_keys.TryGetValue(name.Trim(), out key);

    /// <summary>Name a key is written with in the profile ("1", ",", "Esc", "Space").</summary>
    public static string ProfileName(Key key) => key switch
    {
        Key.Space => "Space",
        Key.LeftShift => "Shift",
        Key.LeftCtrl => "Ctrl",
        Key.LeftAlt => "Alt",
        Key.RightShift or Key.RightCtrl or Key.RightAlt => key.ToString(),
        _ => DisplayName(key),
    };

    /// <summary>Keys in the order they are offered in the editor: letters, digits, F keys, special keys.</summary>
    public static IReadOnlyList<Key> EditorKeys { get; } =
    [
        .. Enum.GetValues<Key>().Where(static k => k.ToString().Length == 1).OrderBy(static k => k.ToString()),
        Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0,
        .. Enum.GetValues<Key>().Where(static k => k.ToString() is ['F', _, ..] && char.IsDigit(k.ToString()[1])),
        Key.Space, Key.Tab, Key.Enter, Key.Escape, Key.Backspace, Key.Grave, Key.Minus, Key.Equals,
        Key.LeftBracket, Key.RightBracket, Key.Backslash, Key.Semicolon, Key.Apostrophe, Key.Comma, Key.Period, Key.Slash,
        Key.Up, Key.Down, Key.Left, Key.Right, Key.Insert, Key.Delete, Key.Home, Key.End, Key.PageUp, Key.PageDown,
        Key.CapsLock, Key.NumLock, Key.LeftShift, Key.LeftCtrl, Key.LeftAlt, Key.RightShift, Key.RightCtrl, Key.RightAlt,
    ];

    public static bool TryParseButton(string name, out GamepadButtons button) => s_buttons.TryGetValue(name.Trim(), out button);

    /// <summary>Name of a key as the user would write it ("8", ",", "Esc"), for the overlay.</summary>
    public static string DisplayName(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => ((int)key - (int)Key.D1 + 1).ToString(),
        Key.D0 => "0",
        Key.Escape => "Esc",
        Key.Comma => ",",
        Key.Period => ".",
        Key.Semicolon => ";",
        Key.Apostrophe => "'",
        Key.Slash => "/",
        Key.Backslash => "\\",
        Key.LeftBracket => "[",
        Key.RightBracket => "]",
        Key.Minus => "-",
        Key.Equals => "=",
        Key.Grave => "`",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftCtrl or Key.RightCtrl => "Ctrl",
        Key.LeftAlt or Key.RightAlt => "Alt",
        Key.Space => "Space",
        _ => key.ToString(),
    };

    /// <summary>Readable chord: "Shift+8", "Alt+left click".</summary>
    public static string DisplayName(in Chord chord)
    {
        string action = chord.Action.Kind switch
        {
            OutputKind.Key => DisplayName(chord.Action.Key),
            OutputKind.Mouse => chord.Action.Button switch
            {
                MouseButton.Left => "left click",
                MouseButton.Right => "right click",
                _ => "middle click",
            },
            _ => string.Empty,
        };

        if (chord.Modifiers == KeyModifiers.None)
        {
            return action;
        }

        var parts = new List<string>(4);
        if ((chord.Modifiers & KeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((chord.Modifiers & KeyModifiers.Ctrl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((chord.Modifiers & KeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        parts.Add(action);
        return string.Join('+', parts);
    }

    /// <summary>Short name in Xbox notation, for the log and the overlay.</summary>
    public static string ButtonName(GamepadButtons button) => button switch
    {
        GamepadButtons.LeftBumper => "LB",
        GamepadButtons.RightBumper => "RB",
        GamepadButtons.LeftTrigger => "LT",
        GamepadButtons.RightTrigger => "RT",
        GamepadButtons.LeftStick => "L3",
        GamepadButtons.RightStick => "R3",
        GamepadButtons.DPadUp => "Up",
        GamepadButtons.DPadDown => "Down",
        GamepadButtons.DPadLeft => "Left",
        GamepadButtons.DPadRight => "Right",
        _ => button.ToString(),
    };

    /// <summary>Name of a set of buttons, e.g. "LT+RT"; "base" if empty.</summary>
    public static string LayerName(GamepadButtons modifiers)
    {
        if (modifiers == GamepadButtons.None)
        {
            return "base";
        }

        return string.Join('+', Enum.GetValues<GamepadButtons>()
            .Where(b => b != GamepadButtons.None && (modifiers & b) != 0)
            .Select(ButtonName));
    }

    private static Dictionary<string, Key> BuildKeys()
    {
        var keys = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase);
        foreach (Key key in Enum.GetValues<Key>())
        {
            keys[key.ToString()] = key;
        }

        for (int digit = 0; digit <= 9; digit++)
        {
            keys[digit.ToString()] = Enum.Parse<Key>("D" + digit);
        }

        keys["Esc"] = Key.Escape;
        keys["Return"] = Key.Enter;
        keys["Ins"] = Key.Insert;
        keys["Del"] = Key.Delete;
        keys["PgUp"] = Key.PageUp;
        keys["PgDn"] = Key.PageDown;
        keys["Caps"] = Key.CapsLock;
        keys["Spacebar"] = Key.Space;
        keys["Shift"] = Key.LeftShift;
        keys["Ctrl"] = Key.LeftCtrl;
        keys["Control"] = Key.LeftCtrl;
        keys["Alt"] = Key.LeftAlt;
        keys[","] = Key.Comma;
        keys["."] = Key.Period;
        keys[";"] = Key.Semicolon;
        keys["'"] = Key.Apostrophe;
        keys["Quote"] = Key.Apostrophe;
        keys["/"] = Key.Slash;
        keys["\\"] = Key.Backslash;
        keys["["] = Key.LeftBracket;
        keys["]"] = Key.RightBracket;
        keys["-"] = Key.Minus;
        keys["="] = Key.Equals;
        keys["`"] = Key.Grave;
        keys["~"] = Key.Grave;
        keys["Tilde"] = Key.Grave;
        return keys;
    }
}

/// <summary>Syntax of the actions in the profiles.</summary>
public static class ActionNames
{
    public const string ActionCamera = "actionCamera";

    /// <summary>
    /// Formats: key ("1", "Space", "Shift+R", "Ctrl+Alt+T"), click ("click:left", "Alt+click:left"),
    /// "wheel:up" / "wheel:down", "actionCamera", "mode:pointer" / "mode:camera" / "mode:toggle",
    /// "radial:&lt;name&gt;".
    /// </summary>
    public static bool TryParse(string text, out BindingAction? action, out string? error)
    {
        action = null;
        error = null;
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            error = "the action is empty";
            return false;
        }

        if (trimmed.Equals(ActionCamera, StringComparison.OrdinalIgnoreCase))
        {
            action = ActionCameraToggleAction.Instance;
            return true;
        }

        int colon = trimmed.IndexOf(':');
        string head = colon < 0 ? trimmed : trimmed[..colon];
        string tail = colon < 0 ? string.Empty : trimmed[(colon + 1)..].Trim();

        if (head.Equals("wheel", StringComparison.OrdinalIgnoreCase))
        {
            action = tail.ToLowerInvariant() switch
            {
                "up" => new WheelAction(1),
                "down" => new WheelAction(-1),
                _ => null,
            };
            error = action is null ? $"'{trimmed}': the wheel only accepts wheel:up or wheel:down" : null;
            return action is not null;
        }

        if (head.Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            action = tail.ToLowerInvariant() switch
            {
                "pointer" => new SetModeAction(CameraMode.Pointer),
                "camera" => new SetModeAction(CameraMode.ActionCamera),
                "toggle" => new SetModeAction(null),
                _ => null,
            };
            error = action is null ? $"'{trimmed}': the mode only accepts mode:pointer, mode:camera or mode:toggle" : null;
            return action is not null;
        }

        if (head.Equals("radial", StringComparison.OrdinalIgnoreCase))
        {
            if (tail.Length == 0)
            {
                error = $"'{trimmed}': the radial name is missing";
                return false;
            }

            action = new RadialAction(tail);
            return true;
        }

        if (!TryParseChord(trimmed, out Chord chord, out error))
        {
            return false;
        }

        action = new ChordAction(chord);
        return true;
    }

    /// <summary>Text of an action as written in the profile (the inverse of <see cref="TryParse"/>).</summary>
    public static string Format(BindingAction action) => action is ChordAction chord ? FormatChord(chord.Chord) : action.ToString()!;

    public static string FormatChord(Chord chord)
    {
        var parts = new List<string>(4);
        if ((chord.Modifiers & KeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((chord.Modifiers & KeyModifiers.Ctrl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((chord.Modifiers & KeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        parts.Add(chord.Action.Kind == OutputKind.Mouse
            ? "click:" + chord.Action.Button.ToString().ToLowerInvariant()
            : KeyNames.ProfileName(chord.Action.Key));
        return string.Join('+', parts);
    }

    public static bool TryParseChord(string text, out Chord chord, out string? error)
    {
        chord = default;
        error = null;
        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);

        if (parts.Length > 1 && parts[^1].Length == 0)
        {
            error = $"'{text}': ends in '+' and there is no key";
            return false;
        }

        KeyModifiers modifiers = KeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            KeyModifiers? modifier = parts[i].ToLowerInvariant() switch
            {
                "shift" => KeyModifiers.Shift,
                "ctrl" or "control" => KeyModifiers.Ctrl,
                "alt" => KeyModifiers.Alt,
                _ => null,
            };
            if (modifier is null)
            {
                error = $"'{text}': '{parts[i]}' is not a modifier (Shift, Ctrl or Alt)";
                return false;
            }

            modifiers |= modifier.Value;
        }

        string last = parts[^1];
        if (last.StartsWith("click:", StringComparison.OrdinalIgnoreCase))
        {
            MouseButton? button = last[6..].Trim().ToLowerInvariant() switch
            {
                "left" => MouseButton.Left,
                "right" => MouseButton.Right,
                "middle" => MouseButton.Middle,
                _ => null,
            };
            if (button is null)
            {
                error = $"'{text}': the click only accepts click:left, click:right or click:middle";
                return false;
            }

            chord = new Chord(modifiers, button.Value);
            return true;
        }

        if (!KeyNames.TryParseKey(last, out Key key))
        {
            error = $"'{text}': unknown key '{last}'";
            return false;
        }

        chord = new Chord(modifiers, key);
        return true;
    }
}

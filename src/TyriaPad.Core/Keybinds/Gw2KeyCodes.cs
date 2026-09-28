using TyriaPad.Core.Output;

namespace TyriaPad.Core.Keybinds;

/// <summary>
/// Key codes of the <c>button</c> attribute in GW2's InputBinds XML. Letters and digits are their
/// ASCII; special keys, the game's own table in nearly alphabetical order (Alt, Ctrl, Shift,
/// apostrophe, backslash, Caps Lock, comma…). Confirmed with the reference XML: 5, 6, 12, 14,
/// 19, 23, 24 and 25; the rest follow from the same order. See docs/gw2-inputbinds-format.md.
/// </summary>
public static class Gw2KeyCodes
{
    private static readonly Key?[] s_special =
    [
        Key.LeftAlt,      // 0
        Key.LeftCtrl,     // 1
        Key.LeftShift,    // 2
        Key.Apostrophe,   // 3
        Key.Backslash,    // 4
        Key.CapsLock,     // 5 (confirmed)
        Key.Comma,        // 6 (confirmed)
        Key.Minus,        // 7
        Key.Equals,       // 8
        Key.Escape,       // 9
        Key.LeftBracket,  // 10
        Key.NumLock,      // 11
        Key.Period,       // 12 (confirmed)
        Key.RightBracket, // 13
        Key.Semicolon,    // 14 (confirmed)
        Key.Slash,        // 15
        null,             // 16 Print Screen: cannot be sent by scancode
        Key.Grave,        // 17
        Key.Backspace,    // 18
        Key.Delete,       // 19 (confirmed)
        Key.Enter,        // 20
        Key.Space,        // 21
        Key.Tab,          // 22
        Key.End,          // 23 (confirmed)
        Key.Home,         // 24 (confirmed)
        Key.Insert,       // 25 (confirmed)
        Key.PageDown,     // 26
        Key.PageUp,       // 27
        Key.Down,         // 28
        Key.Left,         // 29
        Key.Right,        // 30
        Key.Up,           // 31
        Key.F1,           // 32
        Key.F2,
        Key.F3,
        Key.F4,
        Key.F5,
        Key.F6,
        Key.F7,
        Key.F8,
        Key.F9,
        Key.F10,
        Key.F11,
        Key.F12,          // 43
    ];

    private static readonly Key[] s_digits = [Key.D0, Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9];

    private static readonly Key[] s_letters =
    [
        Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H, Key.I, Key.J, Key.K, Key.L, Key.M,
        Key.N, Key.O, Key.P, Key.Q, Key.R, Key.S, Key.T, Key.U, Key.V, Key.W, Key.X, Key.Y, Key.Z,
    ];

    /// <summary>Key for a GW2 code, or false if the code is unknown (numpad, F13+…).</summary>
    public static bool TryToKey(int code, out Key key)
    {
        Key? result = code switch
        {
            >= 0 when code < s_special.Length => s_special[code],
            >= '0' and <= '9' => s_digits[code - '0'],
            >= 'A' and <= 'Z' => s_letters[code - 'A'],
            _ => null,
        };
        key = result.GetValueOrDefault();
        return result is not null;
    }

    /// <summary>GW2 code of a key, or false if it has none.</summary>
    public static bool TryFromKey(Key key, out int code)
    {
        int digit = Array.IndexOf(s_digits, key);
        if (digit >= 0)
        {
            code = '0' + digit;
            return true;
        }

        int letter = Array.IndexOf(s_letters, key);
        if (letter >= 0)
        {
            code = 'A' + letter;
            return true;
        }

        code = Array.IndexOf(s_special, key);
        return code >= 0;
    }
}

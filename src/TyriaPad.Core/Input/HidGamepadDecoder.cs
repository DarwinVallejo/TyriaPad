namespace TyriaPad.Core.Input;

/// <summary>Logical range of an HID axis, as the device descriptor declares it.</summary>
public readonly record struct HidAxisRange(int Min, int Max)
{
    /// <summary>
    /// Corrected range: a 16-bit maximum written as a signed number (0xFFFF = -1) is taken as unsigned.
    /// </summary>
    public static HidAxisRange FromCaps(int logicalMin, int logicalMax, int bitSize)
        => logicalMax < logicalMin && bitSize is > 0 and < 32
            ? new HidAxisRange(logicalMin, (int)((1u << bitSize) - 1))
            : new HidAxisRange(logicalMin, logicalMax);

    /// <summary>Value in 0..1 (clamped when out of range).</summary>
    public float Unit(uint raw) => Max <= Min ? 0f : Math.Clamp(((float)raw - Min) / (Max - Min), 0f, 1f);

    /// <summary>Value in -1..1 with the center of the range at 0.</summary>
    public float Signed(uint raw) => (Unit(raw) * 2f) - 1f;
}

/// <summary>Axes the controller has (HID Generic Desktop page) and their range.</summary>
public sealed record HidGamepadLayout(
    HidAxisRange? X,
    HidAxisRange? Y,
    HidAxisRange? Z,
    HidAxisRange? Rx,
    HidAxisRange? Ry,
    HidAxisRange? Rz,
    HidAxisRange? Hat)
{
    /// <summary>
    /// LT and RT on a single Z axis (the XInput controller seen over HID): LT goes up from the center and RT down.
    /// Pressed together they cancel out, so the LT+RT layer can't be detected.
    /// </summary>
    public bool CombinedTriggers => Z is not null && Rz is null;
}

/// <summary>
/// Turns an already read controller HID report (pressed buttons and axis values) into a reading
/// with XInput ranges. Follows the HID collection Windows gives XInput controllers (<c>IG_</c>):
/// buttons 1–10 = A, B, X, Y, LB, RB, View, Menu, L3, R3; D-pad as a hat; left stick X/Y,
/// right Rx/Ry; triggers on Z (combined) or on Z and Rz (separate).
/// </summary>
public static class HidGamepadDecoder
{
    // HID button (1-based) → XInput bit.
    private static readonly ushort[] s_buttons =
    [
        0x1000, // 1 A
        0x2000, // 2 B
        0x4000, // 3 X
        0x8000, // 4 Y
        0x0100, // 5 LB
        0x0200, // 6 RB
        0x0020, // 7 View
        0x0010, // 8 Menu
        0x0040, // 9 L3
        0x0080, // 10 R3
    ];

    // 8-position hat starting at the top, clockwise → XInput D-pad.
    private static readonly ushort[] s_hat =
    [
        0x1,       // up
        0x1 | 0x8, // up-right
        0x8,       // right
        0x2 | 0x8, // down-right
        0x2,       // down
        0x2 | 0x4, // down-left
        0x4,       // left
        0x1 | 0x4, // up-left
    ];

    /// <param name="buttons">Pressed Button page usages (1 = first button).</param>
    /// <param name="axes">Unprocessed value of each present axis, by usage (0x30 X … 0x39 hat).</param>
    public static RawGamepad Decode(HidGamepadLayout layout, ReadOnlySpan<ushort> buttons, Func<ushort, uint?> axes)
    {
        ushort bits = 0;
        foreach (ushort usage in buttons)
        {
            if (usage >= 1 && usage <= s_buttons.Length)
            {
                bits |= s_buttons[usage - 1];
            }
        }

        if (layout.Hat is { } hat && axes(0x39) is uint hatValue)
        {
            long index = (long)hatValue - hat.Min;
            if (index is >= 0 and < 8 && hat.Max - hat.Min == 7)
            {
                bits |= s_hat[index];
            }
        }

        byte left = 0;
        byte right = 0;
        if (layout.Z is { } z && axes(0x32) is uint zValue)
        {
            if (layout.CombinedTriggers)
            {
                float combined = z.Signed(zValue);
                left = ToTrigger(Math.Max(combined, 0f));
                right = ToTrigger(Math.Max(-combined, 0f));
            }
            else
            {
                left = ToTrigger(z.Unit(zValue));
            }
        }

        if (!layout.CombinedTriggers && layout.Rz is { } rz && axes(0x35) is uint rzValue)
        {
            right = ToTrigger(rz.Unit(rzValue));
        }

        // In HID the Y axis grows downward; in XInput, upward.
        return new RawGamepad(
            bits,
            left,
            right,
            ToThumb(layout.X, axes(0x30), invert: false),
            ToThumb(layout.Y, axes(0x31), invert: true),
            ToThumb(layout.Rx, axes(0x33), invert: false),
            ToThumb(layout.Ry, axes(0x34), invert: true));
    }

    private static byte ToTrigger(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);

    private static short ToThumb(HidAxisRange? range, uint? raw, bool invert)
    {
        if (range is not { } r || raw is not uint value)
        {
            return 0;
        }

        float signed = r.Signed(value) * (invert ? -1f : 1f);
        return (short)Math.Clamp(MathF.Round(signed * 32767f), -32768f, 32767f);
    }
}

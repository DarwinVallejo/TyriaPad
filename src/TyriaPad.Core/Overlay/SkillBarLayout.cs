namespace TyriaPad.Core.Overlay;

/// <summary>Point in pixels relative to the GW2 client area.</summary>
public readonly record struct Point2(float X, float Y)
{
    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Point2 operator *(Point2 a, float factor) => new(a.X * factor, a.Y * factor);

    public float Length => MathF.Sqrt((X * X) + (Y * Y));
}

/// <summary>
/// Skill bar reference points: the center of slots 1, 5 and 10 (the health orb separates 5 from 6,
/// so 1 and 10 aren't enough) and the center of F1 and F2 on the profession bar. The rest is
/// interpolated. Saved per resolution and interface size.
/// </summary>
public sealed record SkillBarCalibration(Point2 Slot1, Point2 Slot5, Point2 Slot10, Point2 F1, Point2 F2);

public readonly record struct SlotGeometry(Point2 Center, float Size);

/// <summary>Where each slot of the skill bar (1–0) and of the profession bar (F1…) is.</summary>
public sealed class SkillBarLayout
{
    public const int SkillSlots = 10;
    public const int MaxProfessionSlots = 8;

    // Measurements with the Normal interface size and Windows at 100% (pixels). GW2 doesn't scale the
    // interface with the resolution, but it does with Windows DPI scaling (option on by default), so
    // they're absolute, centered horizontally and anchored at the bottom, and multiplied by the
    // interface size and the DPI. Tuned with a real calibration on the ROG Ally X (1920x1080,
    // Small interface size, 150%): pitch 71.5 px, 5→6 176 px, F1 102 px above. Calibration mode
    // fixes whatever doesn't line up.
    private const float SlotPitch = 52.8f;
    private const float OrbGap = 130f; // distance between the centers of 5 and 6
    private const float SlotBottomMargin = 41f; // from the bottom edge to the slot center
    private const float ProfessionPitch = 50f;
    private const float ProfessionRise = 75f; // from the skill row to the F1… row
    private const float ProfessionShift = -16f; // F1 starts slightly left of 1

    private const float SlotSizeRatio = 0.9f; // the icon takes up almost the whole pitch
    private const float ProfessionSizeRatio = 0.88f;

    private SkillBarLayout(IReadOnlyList<SlotGeometry> skills, IReadOnlyList<SlotGeometry> profession)
    {
        Skills = skills;
        Profession = profession;
    }

    /// <summary>Slots 1–0, in that order.</summary>
    public IReadOnlyList<SlotGeometry> Skills { get; }

    /// <summary>Slots F1…Fn.</summary>
    public IReadOnlyList<SlotGeometry> Profession { get; }

    public static string KeyFor(int width, int height, UiSize uiSize) => $"{width}x{height}@{uiSize.Key()}";

    /// <summary>Default calibration for a client area, interface size and Windows DPI scale (1 = 100%).</summary>
    public static SkillBarCalibration DefaultCalibration(int width, int height, UiSize uiSize, float dpiScale = 1f)
    {
        float s = uiSize.Scale() * Math.Clamp(dpiScale, 0.5f, 4f);
        float pitch = SlotPitch * s;
        float halfSpan = (4 * pitch) + (OrbGap * s / 2); // from the bar center to slot 1 (or 10)
        float centerX = width / 2f;
        float y = height - (SlotBottomMargin * s);
        var slot1 = new Point2(centerX - halfSpan, y);
        var slot5 = new Point2(slot1.X + (4 * pitch), y);
        var slot10 = new Point2(centerX + halfSpan, y);
        var f1 = new Point2(slot1.X + (ProfessionShift * s), y - (ProfessionRise * s));
        var f2 = f1 + new Point2(ProfessionPitch * s, 0);
        return new SkillBarCalibration(slot1, slot5, slot10, f1, f2);
    }

    public static SkillBarLayout FromCalibration(SkillBarCalibration calibration, int professionSlots)
    {
        Point2 step = (calibration.Slot5 - calibration.Slot1) * 0.25f;
        float size = step.Length * SlotSizeRatio;
        var skills = new SlotGeometry[SkillSlots];
        for (int i = 0; i < 5; i++)
        {
            skills[i] = new SlotGeometry(calibration.Slot1 + (step * i), size);
        }

        // 6–0 mirror 1–5 around the orb: same pitch, anchored at 10.
        for (int i = 5; i < SkillSlots; i++)
        {
            skills[i] = new SlotGeometry(calibration.Slot10 - (step * (SkillSlots - 1 - i)), size);
        }

        Point2 fStep = calibration.F2 - calibration.F1;
        float fSize = fStep.Length * ProfessionSizeRatio;
        int count = Math.Clamp(professionSlots, 0, MaxProfessionSlots);
        var profession = new SlotGeometry[count];
        for (int i = 0; i < count; i++)
        {
            profession[i] = new SlotGeometry(calibration.F1 + (fStep * i), fSize);
        }

        return new SkillBarLayout(skills, profession);
    }
}

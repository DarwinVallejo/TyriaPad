using System.Globalization;
using System.Windows;
using System.Windows.Media;

using TyriaPad.Core.Input;
using TyriaPad.Core.Overlay;

namespace TyriaPad.App.Overlay;

/// <summary>
/// Draws Xbox controller glyphs with vector primitives (no images): dark disc with the colored
/// letter for A/B/X/Y, pills for LB/RB/LT/RT, D-pad with the direction marked, etc.
/// <c>size</c> is the glyph diameter in DrawingContext units.
/// </summary>
internal static class GlyphPainter
{
    public static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    public static readonly Typeface Regular = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static readonly Brush Face = Freeze(new SolidColorBrush(Color.FromArgb(235, 26, 26, 30)));
    public static readonly Brush FaceLight = Freeze(new SolidColorBrush(Color.FromArgb(235, 64, 64, 70)));
    public static readonly Brush White = Brushes.White;
    public static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromRgb(0xF7, 0xC5, 0x31)));
    public static readonly Pen Outline = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1.2));
    public static readonly Pen HoldRing = Freeze(new Pen(Accent, 2) { DashStyle = new DashStyle([2.5, 1.5], 0) });

    private static readonly Brush ColorA = Freeze(new SolidColorBrush(Color.FromRgb(0x7B, 0xC0, 0x43)));
    private static readonly Brush ColorB = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x4C, 0x3D)));
    private static readonly Brush ColorX = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)));
    private static readonly Brush ColorY = Freeze(new SolidColorBrush(Color.FromRgb(0xF7, 0xC5, 0x31)));

    private const double Gap = 0.14; // spacing between the glyphs of a combo, relative to size

    /// <summary>Width a glyph takes (pills are wider than they are tall).</summary>
    public static double WidthOf(GamepadButtons button, double size) => button switch
    {
        GamepadButtons.LeftBumper or GamepadButtons.RightBumper => 1.55 * size,
        GamepadButtons.LeftTrigger or GamepadButtons.RightTrigger or GamepadButtons.M1 or GamepadButtons.M2 => 1.2 * size,
        _ => size,
    };

    /// <summary>
    /// Draws the combo's button centered on <paramref name="center"/> with its gesture mark and, if
    /// asked, the modifiers stacked above (so the combo is never wider than the slot).
    /// </summary>
    public static void DrawStack(DrawingContext dc, ButtonCombo combo, Point center, double size, double modifierSize, bool includeModifiers, double pixelsPerDip)
    {
        Draw(dc, combo.Button, center, size, pixelsPerDip);
        switch (combo.Gesture)
        {
            case GestureKind.Hold:
                // Dashed ring around the button: "hold".
                dc.DrawEllipse(null, HoldRing, center, (WidthOf(combo.Button, size) / 2) + (0.16 * size), (size / 2) + (0.16 * size));
                break;
            case GestureKind.Double:
                FormattedText twice = Text("×2", 0.42 * size, Accent, pixelsPerDip, Bold);
                dc.DrawText(twice, new Point(center.X + (WidthOf(combo.Button, size) / 2) - (twice.Width * 0.35), center.Y - (size / 2) - (twice.Height * 0.55)));
                break;
        }

        if (!includeModifiers)
        {
            return;
        }

        double y = center.Y - (size / 2) - (Gap * size) - (modifierSize / 2);
        foreach (GamepadButtons modifier in combo.Sequence.Where(b => b != combo.Button).Reverse())
        {
            Draw(dc, modifier, new Point(center.X, y), modifierSize, pixelsPerDip);
            y -= modifierSize + (Gap * size);
        }
    }

    public static void Draw(DrawingContext dc, GamepadButtons button, Point center, double size, double pixelsPerDip)
    {
        double r = size / 2;
        switch (button)
        {
            case GamepadButtons.A:
                DrawFaceButton(dc, "A", ColorA, center, size, pixelsPerDip);
                break;
            case GamepadButtons.B:
                DrawFaceButton(dc, "B", ColorB, center, size, pixelsPerDip);
                break;
            case GamepadButtons.X:
                DrawFaceButton(dc, "X", ColorX, center, size, pixelsPerDip);
                break;
            case GamepadButtons.Y:
                DrawFaceButton(dc, "Y", ColorY, center, size, pixelsPerDip);
                break;
            case GamepadButtons.LeftBumper:
            case GamepadButtons.RightBumper:
                DrawPill(dc, button == GamepadButtons.LeftBumper ? "LB" : "RB", center, WidthOf(button, size), 0.72 * size, 0.36 * size, 0.46 * size, pixelsPerDip);
                break;
            case GamepadButtons.LeftTrigger:
            case GamepadButtons.RightTrigger:
                DrawPill(dc, button == GamepadButtons.LeftTrigger ? "LT" : "RT", center, WidthOf(button, size), 0.9 * size, 0.3 * size, 0.46 * size, pixelsPerDip);
                break;
            case GamepadButtons.M1:
            case GamepadButtons.M2:
                // Ally X back buttons: pill with sharp corners to tell them apart from the triggers.
                DrawPill(dc, button == GamepadButtons.M1 ? "M1" : "M2", center, WidthOf(button, size), 0.8 * size, 0.2 * size, 0.44 * size, pixelsPerDip);
                break;
            case GamepadButtons.LeftStick:
            case GamepadButtons.RightStick:
                dc.DrawEllipse(Face, Outline, center, r, r);
                dc.DrawEllipse(FaceLight, null, center, 0.66 * r, 0.66 * r);
                DrawCentered(dc, Text(button == GamepadButtons.LeftStick ? "L3" : "R3", 0.4 * size, White, pixelsPerDip, Bold), center);
                break;
            case GamepadButtons.DPadUp:
            case GamepadButtons.DPadDown:
            case GamepadButtons.DPadLeft:
            case GamepadButtons.DPadRight:
                DrawDPad(dc, button, center, size);
                break;
            case GamepadButtons.Menu:
                dc.DrawEllipse(Face, Outline, center, r, r);
                for (int i = -1; i <= 1; i++)
                {
                    double y = center.Y + (i * 0.22 * size);
                    dc.DrawRoundedRectangle(White, null, new Rect(center.X - (0.28 * size), y - (0.05 * size), 0.56 * size, 0.1 * size), 0.05 * size, 0.05 * size);
                }

                break;
            case GamepadButtons.View:
                dc.DrawEllipse(Face, Outline, center, r, r);
                dc.DrawRoundedRectangle(null, new Pen(White, 0.08 * size), new Rect(center.X - (0.3 * size), center.Y - (0.1 * size), 0.38 * size, 0.38 * size), 0.04 * size, 0.04 * size);
                dc.DrawRoundedRectangle(Face, new Pen(White, 0.08 * size), new Rect(center.X - (0.08 * size), center.Y - (0.3 * size), 0.38 * size, 0.38 * size), 0.04 * size, 0.04 * size);
                break;
            default:
                dc.DrawEllipse(Face, Outline, center, r, r);
                break;
        }
    }

    public static FormattedText Text(string text, double fontSize, Brush brush, double pixelsPerDip, Typeface? typeface = null)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface ?? Bold, fontSize, brush, pixelsPerDip);

    public static void DrawCentered(DrawingContext dc, FormattedText text, Point center)
        => dc.DrawText(text, new Point(center.X - (text.Width / 2), center.Y - (text.Height / 2)));

    private static void DrawFaceButton(DrawingContext dc, string letter, Brush color, Point center, double size, double pixelsPerDip)
    {
        dc.DrawEllipse(Face, Outline, center, size / 2, size / 2);
        DrawCentered(dc, Text(letter, 0.6 * size, color, pixelsPerDip, Bold), new Point(center.X, center.Y + (0.01 * size)));
    }

    private static void DrawPill(DrawingContext dc, string label, Point center, double width, double height, double radius, double fontSize, double pixelsPerDip)
    {
        dc.DrawRoundedRectangle(Face, Outline, new Rect(center.X - (width / 2), center.Y - (height / 2), width, height), radius, radius);
        DrawCentered(dc, Text(label, fontSize, White, pixelsPerDip, Bold), center);
    }

    private static void DrawDPad(DrawingContext dc, GamepadButtons direction, Point center, double size)
    {
        double arm = 0.34 * size;
        double half = size / 2;
        double corner = 0.08 * size;
        dc.DrawRoundedRectangle(Face, null, new Rect(center.X - half, center.Y - (arm / 2), size, arm), corner, corner);
        dc.DrawRoundedRectangle(Face, null, new Rect(center.X - (arm / 2), center.Y - half, arm, size), corner, corner);

        // White arrow in the pressed direction.
        double tip = 0.42 * size;
        double baseOffset = 0.14 * size;
        double baseHalf = 0.13 * size;
        (Point a, Point b, Point c) = direction switch
        {
            GamepadButtons.DPadUp => (new Point(center.X, center.Y - tip), new Point(center.X - baseHalf, center.Y - baseOffset), new Point(center.X + baseHalf, center.Y - baseOffset)),
            GamepadButtons.DPadDown => (new Point(center.X, center.Y + tip), new Point(center.X - baseHalf, center.Y + baseOffset), new Point(center.X + baseHalf, center.Y + baseOffset)),
            GamepadButtons.DPadLeft => (new Point(center.X - tip, center.Y), new Point(center.X - baseOffset, center.Y - baseHalf), new Point(center.X - baseOffset, center.Y + baseHalf)),
            _ => (new Point(center.X + tip, center.Y), new Point(center.X + baseOffset, center.Y - baseHalf), new Point(center.X + baseOffset, center.Y + baseHalf)),
        };
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(a, isFilled: true, isClosed: true);
            ctx.LineTo(b, isStroked: false, isSmoothJoin: false);
            ctx.LineTo(c, isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        dc.DrawGeometry(White, null, geometry);
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

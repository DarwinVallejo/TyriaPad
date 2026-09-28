using System.Windows;
using System.Windows.Media;

using TyriaPad.Core.Config;
using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Overlay;

namespace TyriaPad.App.Overlay;

/// <summary>
/// Draws the whole overlay in <see cref="OnRender"/> from an <see cref="OverlayModel"/>:
/// glyphs over the slots, mode indicator and radial. Works in physical pixels of GW2's client
/// area (the window is placed right on top), converting by the monitor's DPI.
/// </summary>
internal sealed class OverlayCanvas : FrameworkElement
{
    private static readonly Brush BackdropBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x33, 0x3A)));
    private static readonly Brush PanelBrush = Freeze(new SolidColorBrush(Color.FromArgb(200, 18, 18, 22)));
    private static readonly Brush RadialDisc = Freeze(new SolidColorBrush(Color.FromArgb(175, 14, 14, 18)));
    private static readonly Brush RadialSelected = Freeze(new SolidColorBrush(Color.FromArgb(230, 0xF7, 0xC5, 0x31)));
    private static readonly Brush RadialDim = Freeze(new SolidColorBrush(Color.FromArgb(200, 230, 230, 230)));
    private static readonly Pen RadialSeparator = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1));
    private static readonly Pen FramePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(220, 0xF7, 0xC5, 0x31)), 2));
    private static readonly Brush FrameFill = Freeze(new SolidColorBrush(Color.FromArgb(50, 0xF7, 0xC5, 0x31)));
    private static readonly Brush ModeCamera = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)));
    private static readonly Brush ModePointer = Freeze(new SolidColorBrush(Color.FromRgb(0xF7, 0xC5, 0x31)));
    private static readonly Brush ModeMount = Freeze(new SolidColorBrush(Color.FromRgb(0x7B, 0xC0, 0x43)));

    private const double InactiveOpacity = 0.5;

    private OverlayModel? _model;

    public OverlayModel? Model
    {
        get => _model;
        set
        {
            if (!ReferenceEquals(_model, value))
            {
                _model = value;
                InvalidateVisual();
            }
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_model is not { } model)
        {
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double ppd = dpi.PixelsPerDip;
        if (model.Backdrop)
        {
            dc.DrawRectangle(BackdropBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        }

        // From here on, drawing is in physical pixels.
        dc.PushTransform(new ScaleTransform(1 / dpi.DpiScaleX, 1 / dpi.DpiScaleY));
        dc.PushOpacity(model.Settings.Opacity);

        if (model.ShowSlotFrames)
        {
            DrawSlotFrames(dc, model, ppd);
        }

        if (model.ShowSkillBar)
        {
            DrawHints(dc, model.Layout.Skills, model.Skills, 0.5 * model.Settings.Scale, ppd);
            DrawHints(dc, model.Layout.Profession, model.Profession, 0.62 * model.Settings.Scale, ppd);
        }

        if (model.Settings.Indicator != IndicatorCorner.None)
        {
            DrawIndicator(dc, model, ppd);
        }

        if (model.Radial is { } radial)
        {
            DrawRadial(dc, model, radial, ppd);
        }

        dc.Pop();
        dc.Pop();
    }

    private static void DrawHints(DrawingContext dc, IReadOnlyList<SlotGeometry> slots, IReadOnlyList<SlotHint> hints, double glyphRatio, double ppd)
    {
        int count = Math.Min(slots.Count, hints.Count);
        for (int i = 0; i < count; i++)
        {
            if (hints[i].Primary is not { } combo)
            {
                continue;
            }

            SlotGeometry slot = slots[i];
            double size = Math.Max(14, slot.Size * glyphRatio);
            var anchor = new Point(slot.Center.X, slot.Center.Y - (slot.Size / 2));
            if (combo.Active)
            {
                // With the layer active the modifier is already being held: the button is enough.
                GlyphPainter.DrawStack(dc, combo, anchor, size, size, includeModifiers: false, ppd);
            }
            else
            {
                dc.PushOpacity(InactiveOpacity);
                GlyphPainter.DrawStack(dc, combo, anchor, size, size * 0.85, includeModifiers: true, ppd);
                dc.Pop();
            }
        }
    }

    private static void DrawSlotFrames(DrawingContext dc, OverlayModel model, double ppd)
    {
        DrawFrames(dc, model.Layout.Skills, static i => i == 9 ? "0" : (i + 1).ToString(), ppd);
        DrawFrames(dc, model.Layout.Profession, static i => "F" + (i + 1), ppd);
    }

    private static void DrawFrames(DrawingContext dc, IReadOnlyList<SlotGeometry> slots, Func<int, string> label, double ppd)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            SlotGeometry slot = slots[i];
            double half = slot.Size / 2;
            var rect = new Rect(slot.Center.X - half, slot.Center.Y - half, slot.Size, slot.Size);
            dc.DrawRoundedRectangle(FrameFill, FramePen, rect, 4, 4);
            GlyphPainter.DrawCentered(dc, GlyphPainter.Text(label(i), Math.Max(11, slot.Size * 0.32), GlyphPainter.White, ppd), new Point(slot.Center.X, slot.Center.Y));
        }
    }

    private static void DrawIndicator(DrawingContext dc, OverlayModel model, double ppd)
    {
        const double margin = 14;
        double fontSize = 15 * model.Settings.Scale;
        double glyph = 22 * model.Settings.Scale;
        FormattedText text = GlyphPainter.Text(model.IndicatorText, fontSize, GlyphPainter.White, ppd);

        var modifiers = new List<GamepadButtons>();
        foreach (GamepadButtons b in Enum.GetValues<GamepadButtons>())
        {
            if (b != GamepadButtons.None && (model.IndicatorModifiers & b) != 0)
            {
                modifiers.Add(b);
            }
        }

        double dot = 0.55 * fontSize;
        double padding = 0.6 * fontSize;
        double width = padding + dot + (0.5 * fontSize) + text.Width + padding;
        foreach (GamepadButtons b in modifiers)
        {
            width += (0.45 * fontSize) + GlyphPainter.WidthOf(b, glyph);
        }

        double height = Math.Max(text.Height, glyph) + (0.7 * fontSize);
        double x = model.Settings.Indicator is IndicatorCorner.TopLeft or IndicatorCorner.BottomLeft ? margin : model.Bounds.Width - margin - width;
        double y = model.Settings.Indicator is IndicatorCorner.TopLeft or IndicatorCorner.TopRight ? margin : model.Bounds.Height - margin - height;
        var rect = new Rect(x, y, width, height);
        dc.DrawRoundedRectangle(PanelBrush, GlyphPainter.Outline, rect, height / 2, height / 2);

        Brush color = model.IndicatorText switch
        {
            "MOUNT" => ModeMount,
            "CURSOR" => ModePointer,
            _ => ModeCamera,
        };
        double cx = x + padding + (dot / 2);
        double cy = y + (height / 2);
        dc.DrawEllipse(color, null, new Point(cx, cy), dot / 2, dot / 2);
        double tx = cx + (dot / 2) + (0.5 * fontSize);
        dc.DrawText(text, new Point(tx, cy - (text.Height / 2)));
        double gx = tx + text.Width + (0.45 * fontSize);
        foreach (GamepadButtons b in modifiers)
        {
            double w = GlyphPainter.WidthOf(b, glyph);
            GlyphPainter.Draw(dc, b, new Point(gx + (w / 2), cy), glyph, ppd);
            gx += w + (0.45 * fontSize);
        }
    }

    private static void DrawRadial(DrawingContext dc, OverlayModel model, RadialState radial, double ppd)
    {
        int count = radial.Menu.Items.Count;
        if (count == 0)
        {
            return;
        }

        double radius = model.Settings.RadialRadius;
        double ring = Math.Clamp(radius * 0.3, 24, 48);
        double outer = radius + (ring / 2);
        double inner = radius - (ring / 2);
        double hub = inner - 8;
        var center = new Point(model.Bounds.Width / 2.0, model.Bounds.Height / 2.0);
        double sector = Math.Tau / count;

        // Ring with the pointed option in gold; the center stays clear except for the hub with the text.
        dc.DrawEllipse(null, new Pen(RadialDisc, ring), center, radius, radius);
        if (radial.Selected is { } selected)
        {
            dc.DrawGeometry(RadialSelected, null, Wedge(center, inner, outer, (selected * sector) - (sector / 2), sector));
        }

        for (int i = 0; i < count; i++)
        {
            double angle = (i * sector) - (sector / 2);
            dc.DrawLine(RadialSeparator, Polar(center, inner, angle), Polar(center, outer, angle));
        }

        dc.DrawEllipse(RadialDisc, null, center, hub, hub);

        // Labels in pills outside the ring, spaced by their size so they don't touch it.
        double fontSize = Math.Clamp(radius * 0.11, 11, 18);
        double pad = fontSize * 0.55;
        for (int i = 0; i < count; i++)
        {
            bool isSelected = radial.Selected == i;
            RadialItem item = radial.Menu.Items[i];
            FormattedText text = GlyphPainter.Text(item.Label, fontSize, isSelected ? GlyphPainter.Face : GlyphPainter.White, ppd, isSelected ? GlyphPainter.Bold : GlyphPainter.Regular);
            double w = text.Width + (2 * pad);
            double h = text.Height + pad;
            double angle = i * sector;
            double distance = outer + 8 + ((w / 2) * Math.Abs(Math.Sin(angle))) + ((h / 2) * Math.Abs(Math.Cos(angle)));
            Point p = Polar(center, distance, angle);
            dc.DrawRoundedRectangle(isSelected ? RadialSelected : PanelBrush, null, new Rect(p.X - (w / 2), p.Y - (h / 2), w, h), h / 2, h / 2);
            dc.DrawText(text, new Point(p.X - (text.Width / 2), p.Y - (text.Height / 2)));
        }

        // Hub: the pointed option and its key, or the menu name and the stick that drives it.
        string title = radial.SelectedItem?.Label ?? radial.Menu.Name;
        string subtitle = radial.SelectedItem switch
        {
            { Action: ChordAction chord } => KeyNames.DisplayName(chord.Chord),
            { Action: var action } => action.ToString() ?? string.Empty,
            null => radial.Menu.Stick == StickSide.Left ? "left stick" : "right stick",
        };
        FormattedText titleText = GlyphPainter.Text(title, fontSize * 1.15, GlyphPainter.White, ppd);
        titleText.MaxTextWidth = hub * 1.8;
        titleText.TextAlignment = TextAlignment.Center;
        titleText.Trimming = TextTrimming.CharacterEllipsis;
        titleText.MaxLineCount = 2;
        FormattedText subText = GlyphPainter.Text(subtitle, fontSize * 0.85, RadialDim, ppd, GlyphPainter.Regular);
        dc.DrawText(titleText, new Point(center.X - (titleText.MaxTextWidth / 2), center.Y - (titleText.Height / 2) - (subText.Height * 0.55)));
        dc.DrawText(subText, new Point(center.X - (subText.Width / 2), center.Y + (titleText.Height / 2) - (subText.Height * 0.45)));
    }

    private static Point Polar(Point center, double radius, double angle)
        => new(center.X + (radius * Math.Sin(angle)), center.Y - (radius * Math.Cos(angle)));

    private static StreamGeometry Wedge(Point center, double inner, double outer, double start, double sweep)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            bool large = sweep > Math.PI;
            ctx.BeginFigure(Polar(center, outer, start), isFilled: true, isClosed: true);
            ctx.ArcTo(Polar(center, outer, start + sweep), new Size(outer, outer), 0, large, SweepDirection.Clockwise, isStroked: false, isSmoothJoin: false);
            ctx.LineTo(Polar(center, inner, start + sweep), isStroked: false, isSmoothJoin: false);
            ctx.ArcTo(Polar(center, inner, start), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, isStroked: false, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

using TyriaPad.Core.Config;
using TyriaPad.Core.Game;
using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Overlay;

namespace TyriaPad.App.Overlay;

/// <summary>Everything the canvas draws in one frame. Immutable: it is built whole and assigned.</summary>
internal sealed record OverlayModel(
    WindowBounds Bounds,
    SkillBarLayout Layout,
    IReadOnlyList<SlotHint> Skills,
    IReadOnlyList<SlotHint> Profession,
    string IndicatorText,
    GamepadButtons IndicatorModifiers,
    RadialState? Radial,
    OverlaySettings Settings,
    bool ShowSkillBar,
    bool ShowSlotFrames,
    bool Backdrop);

internal static class OverlayModelBuilder
{
    public static OverlayModel Build(
        OverlayState state,
        WindowBounds bounds,
        SkillBarLayout layout,
        OverlaySettings settings,
        bool showSkillBar,
        bool showSlotFrames,
        bool backdrop = false,
        SlotKeys? keys = null)
    {
        return new OverlayModel(
            bounds,
            layout,
            SlotHints.Skills(state.Layout, state.ActiveLayer, keys),
            SlotHints.Profession(state.Layout, state.ActiveLayer, settings.ProfessionSlots, keys),
            IndicatorFor(state),
            state.ActiveLayer.Modifiers,
            state.Radial,
            settings,
            showSkillBar,
            showSlotFrames,
            backdrop);
    }

    public static string IndicatorFor(OverlayState state) => state.Context switch
    {
        GameContext.Mount => "MOUNT",
        GameContext.Pointer => "CURSOR",
        _ => state.Mode == CameraMode.Pointer ? "CURSOR" : "CAMERA",
    };
}

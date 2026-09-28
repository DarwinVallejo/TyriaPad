using TyriaPad.Core.Mapping;

namespace TyriaPad.Core.Overlay;

/// <summary>
/// What the overlay needs from the engine at a given moment: state, mode, context, the context's
/// layers, active layer and open radial. It's a record so the engine only notifies when something changes.
/// </summary>
public sealed record OverlayState(
    EngineState State,
    CameraMode Mode,
    GameContext Context,
    ContextLayout Layout,
    Layer ActiveLayer,
    RadialState? Radial)
{
    public bool IsActive => State == EngineState.Active;
}

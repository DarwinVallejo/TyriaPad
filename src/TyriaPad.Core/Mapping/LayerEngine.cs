using TyriaPad.Core.Input;

namespace TyriaPad.Core.Mapping;

/// <summary>
/// Decides which layer of a context is active based on the held buttons. A layer is a candidate
/// when all its modifiers are pressed; the one with the most modifiers wins. On a tie,
/// the current layer is kept if it is still valid (so LT then LB gives "LT" and LB sends
/// skill 5, and in the opposite order too); otherwise, the first one in the profile.
/// </summary>
public sealed class LayerEngine(ContextLayout layout)
{
    public ContextLayout Layout { get; } = layout;

    public Layer Current { get; private set; } = layout.Base;

    public StickRole LeftStick => Current.LeftStick ?? Layout.LeftStick;

    public StickRole RightStick => Current.RightStick ?? Layout.RightStick;

    /// <summary>Layer that would be active with these buttons held, without changing state.</summary>
    public Layer Peek(GamepadButtons held)
    {
        bool currentValid = (Current.Modifiers & held) == Current.Modifiers;
        Layer best = currentValid ? Current : Layout.Base;
        int bestCount = currentValid ? Current.ModifierCount : -1;
        foreach (Layer layer in Layout.Layers)
        {
            if ((layer.Modifiers & held) == layer.Modifiers && layer.ModifierCount > bestCount)
            {
                best = layer;
                bestCount = layer.ModifierCount;
            }
        }

        return best;
    }

    /// <returns>True if the active layer changed.</returns>
    public bool Update(GamepadButtons held)
    {
        Layer next = Peek(held);
        if (ReferenceEquals(next, Current))
        {
            return false;
        }

        Current = next;
        return true;
    }

    /// <summary>True if pressing this button (on top of the ones already held) would activate another layer.</summary>
    public bool IsModifierPress(GamepadButtons button, GamepadButtons held)
        => !ReferenceEquals(Peek(held | button), Current);

    public void Reset() => Current = Layout.Base;
}

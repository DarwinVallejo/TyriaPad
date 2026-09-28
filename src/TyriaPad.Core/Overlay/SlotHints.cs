using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;

namespace TyriaPad.Core.Overlay;

public enum GestureKind
{
    Press,
    Tap,
    Hold,
    Double,
}

/// <summary>Buttons to press for a slot: the layer's modifiers plus the button.</summary>
/// <param name="Active">The combo's layer is the active one right now (highlighted; the rest are dimmed).</param>
public sealed record ButtonCombo(GamepadButtons Modifiers, GamepadButtons Button, GestureKind Gesture, bool Active)
{
    public GamepadButtons All => Modifiers | Button;

    public int Count => System.Numerics.BitOperations.PopCount((uint)All);

    /// <summary>Buttons in the order they're pressed (modifiers, then the button).</summary>
    public IEnumerable<GamepadButtons> Sequence
    {
        get
        {
            foreach (GamepadButtons b in Enum.GetValues<GamepadButtons>())
            {
                if (b != GamepadButtons.None && (Modifiers & b) != 0)
                {
                    yield return b;
                }
            }

            yield return Button;
        }
    }

    public override string ToString()
        => string.Join('+', Sequence.Select(KeyNames.ButtonName)) + (Gesture switch
        {
            GestureKind.Hold => " (hold)",
            GestureKind.Double => " (2x)",
            _ => string.Empty,
        });
}

/// <summary>A bar slot and the combos that trigger it, the active (or shortest) one first.</summary>
public sealed record SlotHint(string Label, IReadOnlyList<ButtonCombo> Combos)
{
    public ButtonCombo? Primary => Combos.Count > 0 ? Combos[0] : null;
}

/// <summary>
/// Keys (with modifiers) that trigger each slot in GW2: one list per slot, because an action can
/// have a primary and a secondary bind, and empty if the action has no key.
/// </summary>
public sealed record SlotKeys(IReadOnlyList<IReadOnlyList<Chord>> Skills, IReadOnlyList<IReadOnlyList<Chord>> Profession)
{
    /// <summary>GW2's default keys: 1–0 and F1–F8.</summary>
    public static SlotKeys Default { get; } = new(
        new Key[] { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0 }.Select(static k => (IReadOnlyList<Chord>)[k]).ToArray(),
        new Key[] { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8 }.Select(static k => (IReadOnlyList<Chord>)[k]).ToArray());
}

/// <summary>
/// Resolves, for each slot of the skill bar (1–0) and of the profession bar (F1–F8), which button
/// combos trigger it: the chain button → profile key → GW2 action → slot.
/// Each slot's keys come from the GW2 keybinds (<see cref="SlotKeys"/>). With a context
/// without those keys (Cursor) all slots are left empty.
/// </summary>
public static class SlotHints
{
    public const int ProfessionSlotCount = 8;

    private static readonly string[] SkillLabels = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"];
    private static readonly string[] ProfessionLabels = ["F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8"];

    /// <summary>Combos for slots 1–0.</summary>
    public static IReadOnlyList<SlotHint> Skills(ContextLayout layout, Layer activeLayer, SlotKeys? keys = null)
        => Resolve(layout, activeLayer, (keys ?? SlotKeys.Default).Skills, SkillLabels);

    /// <summary>Combos for F1…Fn.</summary>
    public static IReadOnlyList<SlotHint> Profession(ContextLayout layout, Layer activeLayer, int slots, SlotKeys? keys = null)
    {
        IReadOnlyList<IReadOnlyList<Chord>> profession = (keys ?? SlotKeys.Default).Profession;
        int count = Math.Clamp(slots, 0, Math.Min(ProfessionSlotCount, profession.Count));
        return Resolve(layout, activeLayer, profession.Take(count).ToArray(), ProfessionLabels);
    }

    private static IReadOnlyList<SlotHint> Resolve(ContextLayout layout, Layer activeLayer, IReadOnlyList<IReadOnlyList<Chord>> keys, string[] labels)
    {
        var combos = new List<ButtonCombo>[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            combos[i] = [];
        }

        foreach (Layer layer in layout.Layers)
        {
            bool active = layer.Modifiers == activeLayer.Modifiers;
            foreach ((GamepadButtons button, ButtonBinding binding) in layer.Bindings)
            {
                Add(combos, keys, layer, button, binding.Press, GestureKind.Press, active);
                Add(combos, keys, layer, button, binding.Tap, GestureKind.Tap, active);
                Add(combos, keys, layer, button, binding.Hold, GestureKind.Hold, active);
                Add(combos, keys, layer, button, binding.Double, GestureKind.Double, active);
            }
        }

        var hints = new SlotHint[keys.Count];
        for (int i = 0; i < keys.Count; i++)
        {
            // The active one first; then the shortest; on a tie, profile order.
            var ordered = combos[i]
                .Select(static (combo, index) => (combo, index))
                .OrderByDescending(static x => x.combo.Active)
                .ThenBy(static x => x.combo.Count)
                .ThenBy(static x => x.index)
                .Select(static x => x.combo)
                .ToArray();
            hints[i] = new SlotHint(labels[i], ordered);
        }

        return hints;
    }

    private static void Add(List<ButtonCombo>[] combos, IReadOnlyList<IReadOnlyList<Chord>> keys, Layer layer, GamepadButtons button, BindingAction? action, GestureKind gesture, bool active)
    {
        if (action is not ChordAction { Chord: { Action.Kind: OutputKind.Key } chord })
        {
            return;
        }

        var combo = new ButtonCombo(layer.Modifiers, button, gesture, active);
        for (int slot = 0; slot < keys.Count; slot++)
        {
            if (!keys[slot].Contains(chord))
            {
                continue;
            }

            // LB→LT and LT→LB both send 5: it's the same combo, only one is kept.
            int existing = combos[slot].FindIndex(c => c.All == combo.All);
            if (existing < 0)
            {
                combos[slot].Add(combo);
            }
            else if (combo.Active && !combos[slot][existing].Active)
            {
                combos[slot][existing] = combo;
            }
        }
    }
}

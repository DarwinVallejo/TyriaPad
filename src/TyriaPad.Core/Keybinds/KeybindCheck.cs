using TyriaPad.Core.Config;
using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;

namespace TyriaPad.Core.Keybinds;

/// <summary>
/// Cross-checks the profile against the GW2 keybinds and returns readable warnings: keys the profile sends
/// that do nothing in the game, skills with no key or no button, the left stick turning instead
/// of strafing, and the Action Camera key.
/// </summary>
public static class KeybindCheck
{
    // Keys with a fixed meaning in GW2 that don't appear in the keybinds.
    private static readonly HashSet<Chord> s_fixedKeys = [Key.Escape, Key.Enter];

    public static IReadOnlyList<string> Analyze(Profile profile, TyriaPadSettings settings, Gw2Keybinds binds)
    {
        var issues = new List<string>(binds.Warnings);
        CheckProfileKeys(profile, binds, issues);
        CheckMovement(profile, binds, issues);
        CheckActionCamera(profile, settings, binds, issues);
        CheckSlots(profile, settings, binds, issues);
        return issues;
    }

    /// <summary>Key with no action: the profile sends it and in GW2 it does nothing.</summary>
    /// <summary>Key with no action: the profile sends it and in GW2 it does nothing.</summary>
    private static void CheckProfileKeys(Profile profile, Gw2Keybinds binds, List<string> issues)
    {
        // Key → combo → contexts that use it, to say "combat and mount LT+Left" only once.
        var sources = new Dictionary<Chord, Dictionary<string, List<string>>>();
        foreach ((BindingAction action, string where, GameContext? context) in AllActions(profile))
        {
            if (action is not ChordAction { Chord: { Action.Kind: OutputKind.Key } chord }
                || s_fixedKeys.Contains(chord) || binds.ActionsFor(chord).Count > 0)
            {
                continue;
            }

            if (!sources.TryGetValue(chord, out Dictionary<string, List<string>>? combos))
            {
                sources[chord] = combos = [];
            }

            if (!combos.TryGetValue(where, out List<string>? contexts))
            {
                combos[where] = contexts = [];
            }

            if (context is { } c && !contexts.Contains(ContextName(c)))
            {
                contexts.Add(ContextName(c));
            }
        }

        foreach ((Chord chord, Dictionary<string, List<string>> combos) in sources)
        {
            IEnumerable<string> where = combos.Select(static x => x.Value.Count == 0 ? x.Key : $"{string.Join(" and ", x.Value)} {x.Key}");
            issues.Add($"{KeyNames.DisplayName(chord)} has no action in GW2 and the profile uses it in {string.Join(", ", where)}");
        }
    }

    private static void CheckMovement(Profile profile, Gw2Keybinds binds, List<string> issues)
    {
        bool usesMove = profile.Contexts.Values.Any(static c => c.LeftStick == StickRole.Move || c.RightStick == StickRole.Move
            || c.Layers.Any(static l => l.LeftStick == StickRole.Move || l.RightStick == StickRole.Move));
        if (!usesMove)
        {
            return;
        }

        (Key key, Gw2Action wanted, string direction)[] expected =
        [
            (Key.W, Gw2Action.MoveForward, "forward"),
            (Key.S, Gw2Action.MoveBackward, "back"),
            (Key.A, Gw2Action.StrafeLeft, "left"),
            (Key.D, Gw2Action.StrafeRight, "right"),
        ];
        foreach ((Key key, Gw2Action wanted, string direction) in expected)
        {
            IReadOnlyList<Gw2Action> actions = binds.ActionsFor(key);
            if (actions.Contains(wanted))
            {
                continue;
            }

            string now = actions.Count == 0 ? "does nothing" : "does " + string.Join(" and ", actions.Select(binds.NameOf));
            issues.Add($"Left stick {direction} sends {KeyNames.DisplayName(key)}, which in GW2 {now}: bind \"{binds.NameOf(wanted)}\" to {KeyNames.DisplayName(key)}");
        }
    }

    private static void CheckActionCamera(Profile profile, TyriaPadSettings settings, Gw2Keybinds binds, List<string> issues)
    {
        bool usesToggle = AllActions(profile).Any(static x => x.Action is ActionCameraToggleAction);
        if (!usesToggle || !KeyNames.TryParseKey(settings.ActionCameraKey, out Key configured))
        {
            return;
        }

        Gw2Binding binding = binds.BindingOf(Gw2Action.ActionCamera);
        if (binding.Chords.Contains(configured))
        {
            return;
        }

        issues.Add(binding.IsUnbound
            ? $"\"{binds.NameOf(Gw2Action.ActionCamera)}\" has no key in GW2: bind it to {settings.ActionCameraKey} (actionCameraKey in config.json)"
            : $"actionCameraKey is {settings.ActionCameraKey} but in GW2 Action Camera is on {string.Join(" / ", binding.Chords.Select(c => KeyNames.DisplayName(c)))}");
    }

    /// <summary>Action with no key (or no button): skill slots that can't be used with the controller.</summary>
    private static void CheckSlots(Profile profile, TyriaPadSettings settings, Gw2Keybinds binds, List<string> issues)
    {
        var combat = ProfileChords(profile, GameContext.Combat).ToHashSet();
        int professionSlots = Math.Min(settings.Overlay.ProfessionSlots, Gw2Actions.ProfessionSlots.Length);
        foreach (Gw2Action action in Gw2Actions.SkillSlots.Concat(Gw2Actions.ProfessionSlots.Take(professionSlots)))
        {
            Gw2Binding binding = binds.BindingOf(action);
            if (binding.IsUnbound)
            {
                issues.Add($"\"{binds.NameOf(action)}\" has no key in GW2");
            }
            else if (!binding.Chords.Any(combat.Contains))
            {
                issues.Add($"\"{binds.NameOf(action)}\" ({string.Join(" / ", binding.Chords.Select(c => KeyNames.DisplayName(c)))}) has no button in the profile");
            }
        }
    }

    private static IEnumerable<Chord> ProfileChords(Profile profile, GameContext only)
    {
        foreach ((BindingAction action, _, GameContext? context) in AllActions(profile))
        {
            if (context == only
                && action is ChordAction { Chord: { Action.Kind: OutputKind.Key } chord })
            {
                yield return chord;
            }
        }
    }

    /// <summary>All the profile's actions with where they are ("LB+X (tap)", "radial mounts (Raptor)") and their context (null for radials).</summary>
    private static IEnumerable<(BindingAction Action, string Where, GameContext? Context)> AllActions(Profile profile)
    {
        foreach ((GameContext context, ContextLayout layout) in profile.Contexts)
        {
            foreach (Layer layer in layout.Layers)
            {
                foreach ((GamepadButtons button, ButtonBinding binding) in layer.Bindings)
                {
                    string combo = layer.Modifiers == GamepadButtons.None
                        ? KeyNames.ButtonName(button)
                        : $"{KeyNames.LayerName(layer.Modifiers)}+{KeyNames.ButtonName(button)}";
                    foreach ((BindingAction? action, string gesture) in new[] { (binding.Press, ""), (binding.Tap, " (tap)"), (binding.Hold, " (hold)"), (binding.Double, " (2x)") })
                    {
                        if (action is not null)
                        {
                            yield return (action, combo + gesture, context);
                        }
                    }
                }
            }
        }

        foreach (RadialMenu radial in profile.Radials.Values)
        {
            foreach (RadialItem item in radial.Items)
            {
                yield return (item.Action, $"radial {radial.Name} ({item.Label})", null);
            }
        }
    }

    private static string ContextName(GameContext context) => context switch
    {
        GameContext.Pointer => "cursor",
        GameContext.Mount => "mount",
        _ => "combat",
    };
}

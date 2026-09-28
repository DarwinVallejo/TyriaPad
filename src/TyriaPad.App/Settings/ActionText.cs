using TyriaPad.Core.Keybinds;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;

namespace TyriaPad.App.Settings;

/// <summary>Readable texts of the profile actions for the editor ("left click", "Radial \"mounts\"").</summary>
internal static class ActionText
{
    /// <param name="compact">Short version for the map buttons ("◎ mounts" instead of "Radial \"mounts\"").</param>
    public static string Describe(string? action, bool compact = false)
    {
        if (action is null)
        {
            return "—";
        }

        if (!ActionNames.TryParse(action, out BindingAction? parsed, out _))
        {
            return action + " (?)";
        }

        return parsed switch
        {
            ChordAction chord => KeyNames.DisplayName(chord.Chord),
            WheelAction wheel => wheel.Direction > 0 ? "Wheel ↑" : "Wheel ↓",
            ActionCameraToggleAction => "Action Camera",
            SetModeAction mode => mode.Mode switch
            {
                CameraMode.Pointer => "Cursor mode",
                CameraMode.ActionCamera => "Camera mode",
                _ => "Toggle mode",
            },
            RadialAction radial => compact ? "◎ " + radial.Name : $"Radial \"{radial.Name}\"",
            _ => action,
        };
    }

    public static string Describe(BindingDraft? binding)
    {
        if (binding is null || binding.IsEmpty)
        {
            return "—";
        }

        if (binding.Press is not null)
        {
            return Describe(binding.Press);
        }

        var parts = new List<string>(3);
        if (binding.Tap is not null)
        {
            parts.Add(Describe(binding.Tap));
        }

        if (binding.Hold is not null)
        {
            parts.Add("hold " + Describe(binding.Hold));
        }

        if (binding.Double is not null)
        {
            parts.Add("×2 " + Describe(binding.Double));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>What the action's key does in GW2 (according to the exported keybinds), or null.</summary>
    public static string? InGame(string? action, Gw2Keybinds keybinds)
    {
        if (action is null || !ActionNames.TryParse(action, out BindingAction? parsed, out _) || parsed is not ChordAction { Chord: var chord } || chord.Action.Kind != OutputKind.Key)
        {
            return null;
        }

        IReadOnlyList<Gw2Action> actions = keybinds.ActionsFor(chord);
        return actions.Count == 0 ? "no action in GW2" : string.Join(", ", actions.Select(keybinds.NameOf));
    }

    /// <summary>Summary of what a binding's keys do in GW2, for the second line of each button.</summary>
    public static string? InGame(BindingDraft? binding, Gw2Keybinds keybinds)
    {
        if (binding is null)
        {
            return null;
        }

        string?[] parts = binding.Press is not null
            ? [InGame(binding.Press, keybinds)]
            : [InGame(binding.Tap, keybinds), InGame(binding.Hold, keybinds), InGame(binding.Double, keybinds)];
        string[] known = parts.OfType<string>().Where(static p => p != "no action in GW2").ToArray();
        return known.Length > 0 ? string.Join(" · ", known) : null;
    }

    public static string StickRole(string? role) => role?.ToLowerInvariant() switch
    {
        "move" => "Move (WASD)",
        "camera" => "Camera",
        "pointer" => "Cursor",
        "none" => "None",
        null => "—",
        _ => role,
    };

    public static string Context(string name) => name.ToLowerInvariant() switch
    {
        "combat" => "Combat",
        "pointer" => "Cursor",
        "mount" => "Mount",
        _ => name,
    };
}

using TyriaPad.Core.Output;
using TyriaPad.Core.Overlay;

namespace TyriaPad.Core.Keybinds;

/// <summary>
/// Effective GW2 keys: the default ones with the changes of an InputBinds XML on top.
/// Resolves both ways, action → keys and key → actions.
/// </summary>
public sealed class Gw2Keybinds
{
    private readonly Dictionary<Gw2Action, Gw2Binding> _binds;
    private readonly Dictionary<Chord, List<Gw2Action>> _actions = [];
    private readonly IReadOnlyDictionary<Gw2Action, string> _names;

    private Gw2Keybinds(Dictionary<Gw2Action, Gw2Binding> binds, IReadOnlyDictionary<Gw2Action, string> names, string? source, IReadOnlyList<string> warnings)
    {
        _binds = binds;
        _names = names;
        Source = source;
        Warnings = warnings;
        foreach ((Gw2Action action, Gw2Binding binding) in binds)
        {
            foreach (Chord chord in binding.Chords)
            {
                if (!_actions.TryGetValue(chord, out List<Gw2Action>? list))
                {
                    _actions[chord] = list = [];
                }

                list.Add(action);
            }
        }

        SlotKeys = BuildSlotKeys();
    }

    /// <summary>Only the game's default keys (there is no XML or it isn't wanted).</summary>
    public static Gw2Keybinds Defaults { get; } = new(DefaultBinds(), new Dictionary<Gw2Action, string>(), null, []);

    /// <summary>File the changes come from, or null if they are the default values.</summary>
    public string? Source { get; }

    /// <summary>Binds in the XML that could not be read.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Keys of slots 1–0 and F1–F8, for the overlay glyphs.</summary>
    public SlotKeys SlotKeys { get; }

    /// <summary>
    /// Applies an XML over the default values. When a key is assigned, GW2 takes it away from the action
    /// that had it, so a default value that matches a key in the XML disappears.
    /// </summary>
    public static Gw2Keybinds FromFile(InputBindsFile file, string? source)
    {
        var claimed = file.Overrides.Values.SelectMany(static b => b.Chords).ToHashSet();
        var binds = new Dictionary<Gw2Action, Gw2Binding>();
        foreach ((Gw2Action action, Gw2Binding binding) in DefaultBinds())
        {
            if (file.Overrides.ContainsKey(action))
            {
                continue;
            }

            Chord? primary = binding.Primary is { } p && !claimed.Contains(p) ? p : null;
            Chord? secondary = binding.Secondary is { } s && !claimed.Contains(s) ? s : null;
            binds[action] = new Gw2Binding(primary, secondary);
        }

        foreach ((Gw2Action action, Gw2Binding binding) in file.Overrides)
        {
            binds[action] = binding;
        }

        return new Gw2Keybinds(binds, file.Names, source, file.Warnings);
    }

    public Gw2Binding BindingOf(Gw2Action action) => _binds.GetValueOrDefault(action) ?? Gw2Binding.Unbound;

    /// <summary>Actions a key triggers (there can be several: Jump and Swim Up).</summary>
    public IReadOnlyList<Gw2Action> ActionsFor(Chord chord)
        => _actions.TryGetValue(chord, out List<Gw2Action>? list) ? list : [];

    public string NameOf(Gw2Action action) => Gw2Actions.NameOf(action, _names.GetValueOrDefault(action));

    private SlotKeys BuildSlotKeys()
    {
        IReadOnlyList<Chord>[] skills = Gw2Actions.SkillSlots.Select(a => (IReadOnlyList<Chord>)BindingOf(a).Chords.ToArray()).ToArray();

        // GW2 has no eighth profession skill: F8 stays as a literal key.
        IReadOnlyList<Chord>[] profession = Gw2Actions.ProfessionSlots
            .Select(a => (IReadOnlyList<Chord>)BindingOf(a).Chords.ToArray())
            .Append(Overlay.SlotKeys.Default.Profession[^1])
            .ToArray();
        return new SlotKeys(skills, profession);
    }

    private static Dictionary<Gw2Action, Gw2Binding> DefaultBinds()
        => Gw2Actions.All.ToDictionary(static a => a.Action, static a => new Gw2Binding(a.Default, a.Default2));
}

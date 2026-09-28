using System.Text;
using System.Text.Json;

using TyriaPad.Core.Input;

namespace TyriaPad.Core.Mapping;

/// <summary>
/// Profile as written in the file, editable: it keeps the inheritance between contexts and
/// the actions as text. It is what the settings editor modifies. <see cref="ToJson"/> writes it
/// back (without the original's comments) and <see cref="Validate"/> runs it through the same
/// <see cref="ProfileParser"/> the program uses, so nothing invalid reaches the disk.
/// </summary>
public sealed class ProfileDraft
{
    /// <summary>Contexts in file order; the names are "combat", "pointer" and "mount".</summary>
    public static readonly string[] ContextNames = ["combat", "pointer", "mount"];

    /// <summary>Buttons in the order they are written when a new one is added.</summary>
    public static readonly GamepadButtons[] Buttons =
    [
        GamepadButtons.A, GamepadButtons.B, GamepadButtons.X, GamepadButtons.Y,
        GamepadButtons.LeftBumper, GamepadButtons.RightBumper, GamepadButtons.LeftTrigger, GamepadButtons.RightTrigger,
        GamepadButtons.LeftStick, GamepadButtons.RightStick, GamepadButtons.Menu, GamepadButtons.View,
        GamepadButtons.DPadUp, GamepadButtons.DPadDown, GamepadButtons.DPadLeft, GamepadButtons.DPadRight,
        GamepadButtons.M1, GamepadButtons.M2,
    ];

    public string Name { get; set; } = "unnamed";

    public string? Description { get; set; }

    public List<ContextDraft> Contexts { get; } = [];

    public List<RadialDraft> Radials { get; } = [];

    public ContextDraft? FindContext(string name)
        => Contexts.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public RadialDraft? FindRadial(string name)
        => Radials.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What a button does in a layer of a context, following inheritance, and which context it comes from.
    /// null if it does nothing.
    /// </summary>
    public (BindingDraft Binding, ContextDraft From)? Resolve(ContextDraft context, GamepadButtons modifiers, GamepadButtons button)
    {
        for (ContextDraft? current = context; current is not null; current = Parent(current))
        {
            if (current.FindLayer(modifiers)?.Bindings.GetValueOrDefault(button) is { } binding)
            {
                return (binding, current);
            }
        }

        return null;
    }

    /// <summary>Role of a stick in a layer, following inheritance (layer → context → parent).</summary>
    public string? ResolveStick(ContextDraft context, GamepadButtons modifiers, StickSide side)
    {
        for (ContextDraft? current = context; current is not null; current = Parent(current))
        {
            if (current.FindLayer(modifiers) is { } layer && (side == StickSide.Left ? layer.LeftStick : layer.RightStick) is { } role)
            {
                return role;
            }
        }

        for (ContextDraft? current = context; current is not null; current = Parent(current))
        {
            if ((side == StickSide.Left ? current.LeftStick : current.RightStick) is { } role)
            {
                return role;
            }
        }

        return null;
    }

    /// <summary>The context's own and inherited layers, base first and without repeating modifiers.</summary>
    public IReadOnlyList<GamepadButtons> EffectiveLayers(ContextDraft context)
    {
        var chain = new List<ContextDraft>();
        for (ContextDraft? current = context; current is not null && chain.Count < 8; current = Parent(current))
        {
            chain.Insert(0, current);
        }

        var layers = new List<GamepadButtons> { GamepadButtons.None };
        foreach (LayerDraft layer in chain.SelectMany(static c => c.Layers))
        {
            if (!layers.Contains(layer.Modifiers))
            {
                layers.Add(layer.Modifiers);
            }
        }

        return layers;
    }

    /// <summary>
    /// Layer that opens when holding <paramref name="button"/> from layer <paramref name="layer"/>, or
    /// <see cref="GamepadButtons.None"/> if it opens none. A layer is only active with all its
    /// modifiers pressed, so the layer "current + button" has to exist exactly.
    /// </summary>
    public GamepadButtons OpensLayer(ContextDraft context, GamepadButtons layer, GamepadButtons button)
        => (layer & button) == 0 && EffectiveLayers(context).Contains(layer | button) ? layer | button : GamepadButtons.None;

    public ContextDraft? Parent(ContextDraft context)
        => context.Inherits is { } name && !name.Equals(context.Name, StringComparison.OrdinalIgnoreCase) ? FindContext(name) : null;

    /// <summary>Radials used by some button, to warn before deleting one.</summary>
    public bool IsRadialUsed(string name)
    {
        string action = "radial:" + name;
        return Contexts.SelectMany(static c => c.Layers).SelectMany(static l => l.Bindings.Values)
            .Any(b => new[] { b.Press, b.Hold }.Any(a => a is not null && a.Equals(action, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Renames a radial and the buttons that open it.</summary>
    public void RenameRadial(RadialDraft radial, string name)
    {
        string from = "radial:" + radial.Name;
        string to = "radial:" + name;
        string? Swap(string? action) => action is not null && action.Equals(from, StringComparison.OrdinalIgnoreCase) ? to : action;
        foreach (LayerDraft layer in Contexts.SelectMany(static c => c.Layers))
        {
            foreach ((GamepadButtons button, BindingDraft binding) in layer.Bindings.ToList())
            {
                layer.Bindings[button] = binding with { Press = Swap(binding.Press), Hold = Swap(binding.Hold) };
            }
        }

        radial.Name = name;
    }

    /// <summary>Checks the draft with the program's parser. Throws <see cref="ConfigException"/>.</summary>
    public Profile Validate() => ProfileParser.Parse(ToJson());

    public ProfileDraft Clone() => FromJson(ToJson());

    public static ProfileDraft FromJson(string json)
    {
        ProfileParser.ProfileDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ProfileParser.ProfileDocument>(json, ProfileParser.JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"invalid JSON: {ex.Message}");
        }

        if (document is null)
        {
            throw new ConfigException("the profile is empty");
        }

        var draft = new ProfileDraft { Name = document.Name ?? "unnamed", Description = document.Description };
        foreach ((string name, ProfileParser.ContextDocument context) in document.Contexts ?? [])
        {
            var contextDraft = new ContextDraft(name.ToLowerInvariant())
            {
                Inherits = context.Inherits,
                LeftStick = context.LeftStick,
                RightStick = context.RightStick,
            };
            foreach ((string layerName, ProfileParser.LayerDocument layer) in context.Layers ?? [])
            {
                if (!ProfileParser.TryParseLayerName(layerName, out GamepadButtons modifiers, out string? error))
                {
                    throw new ConfigException($"contexts.{name}.layers.{layerName}: {error}");
                }

                var layerDraft = new LayerDraft(modifiers) { LeftStick = layer.LeftStick, RightStick = layer.RightStick };
                foreach ((string buttonName, JsonElement element) in layer.Buttons ?? [])
                {
                    if (!KeyNames.TryParseButton(buttonName, out GamepadButtons button))
                    {
                        throw new ConfigException($"contexts.{name}.layers.{layerName}.{buttonName}: unknown button");
                    }

                    layerDraft.Bindings[button] = ReadBinding(element, $"contexts.{name}.layers.{layerName}.{buttonName}");
                }

                contextDraft.Layers.Add(layerDraft);
            }

            draft.Contexts.Add(contextDraft);
        }

        foreach ((string name, ProfileParser.RadialDocument radial) in document.Radials ?? [])
        {
            var radialDraft = new RadialDraft(name) { Stick = radial.Stick ?? "right" };
            foreach (ProfileParser.RadialItemDocument item in radial.Items ?? [])
            {
                radialDraft.Items.Add(new RadialItemDraft(item.Label ?? item.Action ?? string.Empty, item.Action ?? string.Empty));
            }

            draft.Radials.Add(radialDraft);
        }

        return draft;
    }

    private static BindingDraft ReadBinding(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return new BindingDraft { Press = element.GetString() };
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ConfigException($"{path}: must be a string or an object");
        }

        string? Read(string name)
            => element.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: JsonValueKind.String } p
                ? p.Value.GetString()
                : null;

        return new BindingDraft { Press = Read("press"), Tap = Read("tap"), Hold = Read("hold"), Double = Read("double") };
    }

    /// <summary>JSON with two-space indentation; each button and each radial option on one line.</summary>
    public string ToJson()
    {
        var json = new StringBuilder();
        json.Append("{\n");
        json.Append("  // Saved by the TyriaPad editor. The format is described in comments in profiles/blaggletoad.json.\n");
        json.Append($"  \"name\": {Quote(Name)},\n");
        if (!string.IsNullOrWhiteSpace(Description))
        {
            json.Append($"  \"description\": {Quote(Description)},\n");
        }

        json.Append("  \"contexts\": {");
        AppendList(json, Contexts, "    ", (context, indent) =>
        {
            var members = new List<string>();
            if (!string.IsNullOrWhiteSpace(context.Inherits))
            {
                members.Add($"\"inherits\": {Quote(context.Inherits)}");
            }

            if (context.LeftStick is not null)
            {
                members.Add($"\"leftStick\": {Quote(context.LeftStick)}");
            }

            if (context.RightStick is not null)
            {
                members.Add($"\"rightStick\": {Quote(context.RightStick)}");
            }

            var layers = new StringBuilder("\"layers\": {");
            AppendList(layers, context.Layers, indent + "    ", (layer, layerIndent) =>
            {
                var entries = new List<string>();
                if (layer.LeftStick is not null)
                {
                    entries.Add($"\"leftStick\": {Quote(layer.LeftStick)}");
                }

                if (layer.RightStick is not null)
                {
                    entries.Add($"\"rightStick\": {Quote(layer.RightStick)}");
                }

                entries.AddRange(layer.Bindings.Where(static b => !b.Value.IsEmpty).Select(static b => $"{Quote(KeyNames.ButtonName(b.Key))}: {b.Value.ToJson()}"));
                return $"{Quote(layer.Name)}: {Block(entries, layerIndent)}";
            });
            layers.Append($"\n{indent}  }}");
            members.Add(layers.ToString());
            return $"{Quote(context.Name)}: {Block(members, indent)}";
        });
        json.Append("\n  }");

        if (Radials.Count > 0)
        {
            json.Append(",\n  \"radials\": {");
            AppendList(json, Radials, "    ", (radial, indent) =>
            {
                string items = string.Join(",\n", radial.Items.Select(i => $"{indent}    {{ \"label\": {Quote(i.Label)}, \"action\": {Quote(i.Action)} }}"));
                return $"{Quote(radial.Name)}: {{\n{indent}  \"stick\": {Quote(radial.Stick)},\n{indent}  \"items\": [\n{items}\n{indent}  ]\n{indent}}}";
            });
            json.Append("\n  }");
        }

        json.Append("\n}\n");
        return json.ToString();
    }

    private static void AppendList<T>(StringBuilder json, IEnumerable<T> items, string indent, Func<T, string, string> write)
    {
        bool first = true;
        foreach (T item in items)
        {
            json.Append(first ? "\n" : ",\n").Append(indent).Append(write(item, indent));
            first = false;
        }
    }

    private static string Block(List<string> members, string indent)
        => members.Count == 0 ? "{}" : "{\n" + string.Join(",\n", members.Select(m => indent + "  " + m)) + $"\n{indent}}}";

    internal static string Quote(string text) => JsonSerializer.Serialize(text, s_quote);

    // Without escaping accents or symbols: the file is read by a person.
    private static readonly JsonSerializerOptions s_quote = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}

public sealed class ContextDraft(string name)
{
    public string Name { get; } = name;

    public string? Inherits { get; set; }

    public string? LeftStick { get; set; }

    public string? RightStick { get; set; }

    public List<LayerDraft> Layers { get; } = [];

    public LayerDraft? FindLayer(GamepadButtons modifiers) => Layers.FirstOrDefault(l => l.Modifiers == modifiers);

    /// <summary>The layer of its own with those modifiers; if it doesn't exist it is created (empty).</summary>
    public LayerDraft GetOrAddLayer(GamepadButtons modifiers)
    {
        if (FindLayer(modifiers) is { } layer)
        {
            return layer;
        }

        layer = new LayerDraft(modifiers);
        if (modifiers == GamepadButtons.None)
        {
            Layers.Insert(0, layer);
        }
        else
        {
            Layers.Add(layer);
        }

        return layer;
    }
}

public sealed class LayerDraft(GamepadButtons modifiers)
{
    public GamepadButtons Modifiers { get; } = modifiers;

    public string Name => KeyNames.LayerName(Modifiers);

    public string? LeftStick { get; set; }

    public string? RightStick { get; set; }

    /// <summary>Button → binding, in file order (new ones at the end).</summary>
    public Dictionary<GamepadButtons, BindingDraft> Bindings { get; } = [];
}

/// <summary>
/// Binding as text. Only <see cref="Press"/> = held while pressed; otherwise, gestures.
/// </summary>
public sealed record BindingDraft
{
    public string? Press { get; init; }

    public string? Tap { get; init; }

    public string? Hold { get; init; }

    public string? Double { get; init; }

    public bool IsEmpty => Press is null && Tap is null && Hold is null && Double is null;

    public bool UsesGestures => Press is null && !IsEmpty;

    public string ToJson()
    {
        if (Press is not null && Tap is null && Hold is null && Double is null)
        {
            return ProfileDraft.Quote(Press);
        }

        var parts = new List<string>(4);
        void Add(string name, string? value)
        {
            if (value is not null)
            {
                parts.Add($"\"{name}\": {ProfileDraft.Quote(value)}");
            }
        }

        Add("press", Press);
        Add("tap", Tap);
        Add("hold", Hold);
        Add("double", Double);
        return "{ " + string.Join(", ", parts) + " }";
    }

    public override string ToString()
    {
        if (Press is not null)
        {
            return Press;
        }

        var parts = new List<string>(3);
        if (Tap is not null)
        {
            parts.Add(Tap);
        }

        if (Hold is not null)
        {
            parts.Add("hold " + Hold);
        }

        if (Double is not null)
        {
            parts.Add("×2 " + Double);
        }

        return string.Join(" · ", parts);
    }
}

public sealed class RadialDraft(string name)
{
    public string Name { get; set; } = name;

    /// <summary>"left" or "right".</summary>
    public string Stick { get; set; } = "right";

    public List<RadialItemDraft> Items { get; } = [];
}

public sealed record RadialItemDraft(string Label, string Action);

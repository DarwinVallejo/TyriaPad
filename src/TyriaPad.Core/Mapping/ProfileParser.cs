using System.Text.Json;
using System.Text.Json.Serialization;

using TyriaPad.Core.Input;

namespace TyriaPad.Core.Mapping;

/// <summary>User-readable configuration error (JSON path + reason, one line per failure).</summary>
public sealed class ConfigException(string message) : Exception(message);

/// <summary>
/// Turns a profile's JSON into a validated <see cref="Profile"/>. Collects all the errors
/// so the user can fix them in one go. Format:
/// <code>
/// {
///   "name": "...",
///   "contexts": {
///     "combat": { "leftStick": "move", "rightStick": "camera", "layers": { "base": { "RB": "1", ... }, "LB": { ... } } },
///     "mount":  { "inherits": "combat", "layers": { "base": { "X": "C" } } },
///     "pointer": { ... }
///   },
///   "radials": { "mounts": { "stick": "right", "items": [ { "label": "Raptor", "action": "Shift+R" } ] } }
/// }
/// </code>
/// A binding is a string (held while the button is pressed) or an object with
/// <c>press</c> / <c>tap</c> / <c>hold</c> / <c>double</c>.
/// </summary>
public static class ProfileParser
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private const int MaxInheritanceDepth = 4;

    public static Profile Parse(string json)
    {
        ProfileDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ProfileDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"invalid JSON: {ex.Message}");
        }

        if (document is null)
        {
            throw new ConfigException("the profile is empty");
        }

        var errors = new List<string>();
        var radials = ParseRadials(document.Radials, errors);
        var contexts = ParseContexts(document.Contexts, radials, errors);

        if (errors.Count > 0)
        {
            throw new ConfigException(string.Join(Environment.NewLine, errors));
        }

        return new Profile(document.Name ?? "unnamed", contexts, radials);
    }

    private static Dictionary<string, RadialMenu> ParseRadials(Dictionary<string, RadialDocument>? documents, List<string> errors)
    {
        var radials = new Dictionary<string, RadialMenu>(StringComparer.OrdinalIgnoreCase);
        if (documents is null)
        {
            return radials;
        }

        foreach ((string name, RadialDocument document) in documents)
        {
            string path = $"radials.{name}";
            StickSide? stick = document.Stick?.ToLowerInvariant() switch
            {
                "left" => StickSide.Left,
                "right" => StickSide.Right,
                _ => null,
            };
            if (stick is null)
            {
                errors.Add($"{path}.stick: must be \"left\" or \"right\"");
            }

            var items = new List<RadialItem>();
            if (document.Items is null || document.Items.Count < 2)
            {
                errors.Add($"{path}.items: at least two options are needed");
            }
            else
            {
                for (int i = 0; i < document.Items.Count; i++)
                {
                    RadialItemDocument item = document.Items[i];
                    string itemPath = $"{path}.items[{i}]";
                    if (string.IsNullOrWhiteSpace(item.Action))
                    {
                        errors.Add($"{itemPath}.action: the action is missing");
                        continue;
                    }

                    if (!ActionNames.TryParse(item.Action, out BindingAction? action, out string? error))
                    {
                        errors.Add($"{itemPath}.action: {error}");
                        continue;
                    }

                    if (action is RadialAction)
                    {
                        errors.Add($"{itemPath}.action: a radial can't open another radial");
                        continue;
                    }

                    items.Add(new RadialItem(item.Label ?? item.Action, action!));
                }
            }

            if (stick is not null && items.Count >= 2)
            {
                radials[name] = new RadialMenu(name, stick.Value, items);
            }
        }

        return radials;
    }

    private static Dictionary<GameContext, ContextLayout> ParseContexts(
        Dictionary<string, ContextDocument>? documents,
        Dictionary<string, RadialMenu> radials,
        List<string> errors)
    {
        var result = new Dictionary<GameContext, ContextLayout>();
        if (documents is null || documents.Count == 0)
        {
            errors.Add("contexts: the section is missing; at least the \"combat\" context is needed");
            return result;
        }

        var byName = new Dictionary<string, (GameContext Context, ContextDocument Document)>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, ContextDocument document) in documents)
        {
            GameContext? context = name.ToLowerInvariant() switch
            {
                "combat" => GameContext.Combat,
                "pointer" => GameContext.Pointer,
                "mount" => GameContext.Mount,
                _ => null,
            };
            if (context is null)
            {
                errors.Add($"contexts.{name}: the contexts are \"combat\", \"pointer\" and \"mount\"");
                continue;
            }

            byName[name] = (context.Value, document);
        }

        if (!byName.ContainsKey("combat"))
        {
            errors.Add("contexts: the \"combat\" context is missing");
        }

        var resolved = new Dictionary<string, ContextLayout>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in byName.Keys)
        {
            ContextLayout? layout = Resolve(name, byName, resolved, radials, errors, depth: 0);
            if (layout is not null)
            {
                result[layout.Context] = layout;
            }
        }

        return result;
    }

    private static ContextLayout? Resolve(
        string name,
        Dictionary<string, (GameContext Context, ContextDocument Document)> byName,
        Dictionary<string, ContextLayout> resolved,
        Dictionary<string, RadialMenu> radials,
        List<string> errors,
        int depth)
    {
        if (resolved.TryGetValue(name, out ContextLayout? done))
        {
            return done;
        }

        (GameContext context, ContextDocument document) = byName[name];
        string path = $"contexts.{name}";
        ContextLayout? parent = null;
        if (!string.IsNullOrWhiteSpace(document.Inherits))
        {
            if (depth >= MaxInheritanceDepth || document.Inherits.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{path}.inherits: circular inheritance");
                return null;
            }

            if (!byName.ContainsKey(document.Inherits))
            {
                errors.Add($"{path}.inherits: context \"{document.Inherits}\" does not exist");
                return null;
            }

            parent = Resolve(document.Inherits, byName, resolved, radials, errors, depth + 1);
            if (parent is null)
            {
                return null;
            }
        }

        StickRole? left = ParseStickRole(document.LeftStick, $"{path}.leftStick", errors);
        StickRole? right = ParseStickRole(document.RightStick, $"{path}.rightStick", errors);

        var layers = new List<Layer>();
        if (parent is not null)
        {
            layers.AddRange(parent.Layers);
        }

        foreach ((string layerName, LayerDocument layerDocument) in document.Layers ?? [])
        {
            string layerPath = $"{path}.layers.{layerName}";
            if (!TryParseLayerName(layerName, out GamepadButtons modifiers, out string? nameError))
            {
                errors.Add($"{layerPath}: {nameError}");
                continue;
            }

            Dictionary<GamepadButtons, ButtonBinding> bindings = ParseBindings(layerDocument, modifiers, layerPath, radials, errors);
            StickRole? layerLeft = ParseStickRole(layerDocument.LeftStick, $"{layerPath}.leftStick", errors);
            StickRole? layerRight = ParseStickRole(layerDocument.RightStick, $"{layerPath}.rightStick", errors);

            int existing = layers.FindIndex(l => l.Modifiers == modifiers);
            if (existing >= 0)
            {
                Layer inherited = layers[existing];
                var merged = new Dictionary<GamepadButtons, ButtonBinding>(inherited.Bindings);
                foreach ((GamepadButtons button, ButtonBinding binding) in bindings)
                {
                    merged[button] = binding;
                }

                layers[existing] = inherited with
                {
                    Bindings = merged,
                    LeftStick = layerLeft ?? inherited.LeftStick,
                    RightStick = layerRight ?? inherited.RightStick,
                };
            }
            else
            {
                layers.Add(new Layer(KeyNames.LayerName(modifiers), modifiers, layerLeft, layerRight, bindings));
            }
        }

        if (!layers.Any(static l => l.Modifiers == GamepadButtons.None))
        {
            layers.Insert(0, new Layer("base", GamepadButtons.None, null, null, new Dictionary<GamepadButtons, ButtonBinding>()));
        }

        var layout = new ContextLayout(
            context,
            left ?? parent?.LeftStick ?? StickRole.None,
            right ?? parent?.RightStick ?? StickRole.None,
            layers);
        resolved[name] = layout;
        return layout;
    }

    private static Dictionary<GamepadButtons, ButtonBinding> ParseBindings(
        LayerDocument layer,
        GamepadButtons modifiers,
        string layerPath,
        Dictionary<string, RadialMenu> radials,
        List<string> errors)
    {
        var bindings = new Dictionary<GamepadButtons, ButtonBinding>();
        foreach ((string buttonName, JsonElement element) in layer.Buttons ?? [])
        {
            string path = $"{layerPath}.{buttonName}";
            if (!KeyNames.TryParseButton(buttonName, out GamepadButtons button))
            {
                errors.Add($"{path}: unknown button (A, B, X, Y, LB, RB, LT, RT, L3, R3, Menu, View, Up, Down, Left, Right)");
                continue;
            }

            if ((modifiers & button) != 0)
            {
                errors.Add($"{path}: {buttonName} is a modifier of this layer and can't have an action in it");
                continue;
            }

            ButtonBinding? binding = ParseBinding(element, path, radials, errors);
            if (binding is not null)
            {
                bindings[button] = binding;
            }
        }

        return bindings;
    }

    private static ButtonBinding? ParseBinding(JsonElement element, string path, Dictionary<string, RadialMenu> radials, List<string> errors)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            BindingAction? press = ParseAction(element.GetString()!, path, radials, errors, allowRadial: true);
            return press is null ? null : new ButtonBinding(Press: press);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}: must be a string (\"1\") or an object ({{ \"tap\": ..., \"hold\": ... }})");
            return null;
        }

        BindingAction? pressAction = null;
        BindingAction? tap = null;
        BindingAction? hold = null;
        BindingAction? twice = null;
        bool ok = true;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                errors.Add($"{path}.{property.Name}: must be a string");
                ok = false;
                continue;
            }

            string text = property.Value.GetString()!;
            string gesturePath = $"{path}.{property.Name}";
            switch (property.Name.ToLowerInvariant())
            {
                case "press":
                    pressAction = ParseAction(text, gesturePath, radials, errors, allowRadial: true);
                    ok &= pressAction is not null;
                    break;
                case "tap":
                    tap = ParseAction(text, gesturePath, radials, errors, allowRadial: false);
                    ok &= tap is not null;
                    break;
                case "hold":
                    hold = ParseAction(text, gesturePath, radials, errors, allowRadial: true);
                    ok &= hold is not null;
                    break;
                case "double":
                    twice = ParseAction(text, gesturePath, radials, errors, allowRadial: false);
                    ok &= twice is not null;
                    break;
                default:
                    errors.Add($"{gesturePath}: only press, tap, hold and double are allowed");
                    ok = false;
                    break;
            }
        }

        if (!ok)
        {
            return null;
        }

        if (pressAction is not null && (tap ?? hold ?? twice) is not null)
        {
            errors.Add($"{path}: \"press\" doesn't combine with tap/hold/double (press already covers press and release)");
            return null;
        }

        if (pressAction is null && tap is null && hold is null && twice is null)
        {
            errors.Add($"{path}: the binding is empty");
            return null;
        }

        // Only tap, nothing to tell apart: it is held while pressed so there is no delay.
        if (pressAction is null && hold is null && twice is null)
        {
            return new ButtonBinding(Press: tap);
        }

        return new ButtonBinding(pressAction, tap, hold, twice);
    }

    private static BindingAction? ParseAction(string text, string path, Dictionary<string, RadialMenu> radials, List<string> errors, bool allowRadial)
    {
        if (!ActionNames.TryParse(text, out BindingAction? action, out string? error))
        {
            errors.Add($"{path}: {error}");
            return null;
        }

        if (action is RadialAction radial)
        {
            if (!allowRadial)
            {
                errors.Add($"{path}: a radial can only be opened with \"hold\" or \"press\" (the choice is made on release)");
                return null;
            }

            if (!radials.ContainsKey(radial.Name))
            {
                errors.Add($"{path}: radial \"{radial.Name}\" does not exist");
                return null;
            }
        }

        return action;
    }

    private static StickRole? ParseStickRole(string? text, string path, List<string> errors)
    {
        if (text is null)
        {
            return null;
        }

        StickRole? role = text.Trim().ToLowerInvariant() switch
        {
            "none" => StickRole.None,
            "move" => StickRole.Move,
            "camera" => StickRole.Camera,
            "pointer" => StickRole.Pointer,
            _ => null,
        };
        if (role is null)
        {
            errors.Add($"{path}: must be \"move\", \"camera\", \"pointer\" or \"none\"");
        }

        return role;
    }

    internal static bool TryParseLayerName(string name, out GamepadButtons modifiers, out string? error)
    {
        modifiers = GamepadButtons.None;
        error = null;
        if (name.Trim().Equals("base", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string part in name.Split('+', StringSplitOptions.TrimEntries))
        {
            if (!KeyNames.TryParseButton(part, out GamepadButtons button))
            {
                error = $"a layer name is \"base\" or the modifiers joined with '+', e.g. \"LB\" or \"LT+RT\" ('{part}' is not a button)";
                return false;
            }

            if ((modifiers & button) != 0)
            {
                error = $"modifier {part} is repeated";
                return false;
            }

            modifiers |= button;
        }

        return true;
    }

    internal sealed class ProfileDocument
    {
        public string? Name { get; set; }

        public string? Description { get; set; }

        public Dictionary<string, ContextDocument>? Contexts { get; set; }

        public Dictionary<string, RadialDocument>? Radials { get; set; }
    }

    internal sealed class ContextDocument
    {
        public string? Inherits { get; set; }

        public string? LeftStick { get; set; }

        public string? RightStick { get; set; }

        public Dictionary<string, LayerDocument>? Layers { get; set; }
    }

    internal sealed class LayerDocument
    {
        public string? LeftStick { get; set; }

        public string? RightStick { get; set; }

        /// <summary>Anything that isn't a known property is a button with its binding.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Buttons { get; set; }
    }

    internal sealed class RadialDocument
    {
        public string? Stick { get; set; }

        public List<RadialItemDocument>? Items { get; set; }
    }

    internal sealed class RadialItemDocument
    {
        public string? Label { get; set; }

        public string? Action { get; set; }
    }
}

using System.Globalization;
using System.Xml;
using System.Xml.Linq;

using TyriaPad.Core.Output;

namespace TyriaPad.Core.Keybinds;

/// <summary>Primary and secondary bind of an action; null = no key (or one TyriaPad can't read).</summary>
public sealed record Gw2Binding(Chord? Primary, Chord? Secondary)
{
    public static readonly Gw2Binding Unbound = new(null, null);

    public IEnumerable<Chord> Chords
    {
        get
        {
            if (Primary is { } primary)
            {
                yield return primary;
            }

            if (Secondary is { } secondary && secondary != Primary)
            {
                yield return secondary;
            }
        }
    }

    public bool IsUnbound => Primary is null && Secondary is null;
}

/// <summary>What an InputBinds XML contains: only the actions changed from the default values.</summary>
/// <param name="Names">The <c>name</c> of each action, to name the ones TyriaPad doesn't know.</param>
/// <param name="Warnings">Binds that could not be read (mouse, unknown codes…).</param>
public sealed record InputBindsFile(
    IReadOnlyDictionary<Gw2Action, Gw2Binding> Overrides,
    IReadOnlyDictionary<Gw2Action, string> Names,
    IReadOnlyList<string> Warnings);

/// <summary>Reads the XML GW2 saves in <c>Documents\Guild Wars 2\InputBinds</c>.</summary>
public static class InputBindsParser
{
    public static InputBindsFile Parse(string xml)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new FormatException($"invalid XML: {ex.Message}", ex);
        }

        if (document.Root is not { Name.LocalName: "InputBindings" } root)
        {
            throw new FormatException("not a GW2 keybinds file (missing <InputBindings>)");
        }

        var overrides = new Dictionary<Gw2Action, Gw2Binding>();
        var names = new Dictionary<Gw2Action, string>();
        var warnings = new List<string>();
        foreach (XElement element in root.Elements("action"))
        {
            string name = (string?)element.Attribute("name") ?? string.Empty;
            if (!int.TryParse((string?)element.Attribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                warnings.Add($"action \"{name}\" has no id: ignored");
                continue;
            }

            var action = (Gw2Action)id;
            names[action] = name;
            string label = Gw2Actions.NameOf(action, name);
            overrides[action] = new Gw2Binding(
                ReadBind(element, "device", "button", "mod", label, "primary", warnings),
                ReadBind(element, "device2", "button2", "mod2", label, "secondary", warnings));
        }

        return new InputBindsFile(overrides, names, warnings);
    }

    private static Chord? ReadBind(XElement element, string deviceAttribute, string buttonAttribute, string modAttribute, string label, string which, List<string> warnings)
    {
        string? device = (string?)element.Attribute(deviceAttribute);
        string? button = (string?)element.Attribute(buttonAttribute);
        if (button is null || device is "None" or "Unset")
        {
            return null;
        }

        if (!int.TryParse(button, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
        {
            warnings.Add($"{label}: {which} bind with non-numeric code \"{button}\"");
            return null;
        }

        int mod = 0;
        if ((string?)element.Attribute(modAttribute) is { } modText
            && !int.TryParse(modText, NumberStyles.Integer, CultureInfo.InvariantCulture, out mod))
        {
            warnings.Add($"{label}: {which} bind with non-numeric modifier \"{modText}\"");
            return null;
        }

        var modifiers = (KeyModifiers)(mod & 7);
        if (device is not null && !device.Equals("Keyboard", StringComparison.OrdinalIgnoreCase))
        {
            // Mouse buttons can't be matched against what the profile sends: they are just noted.
            warnings.Add($"{label}: {which} bind on {device} (button {code}), not taken into account");
            return null;
        }

        if (!Gw2KeyCodes.TryToKey(code, out Key key))
        {
            warnings.Add($"{label}: {which} bind with unknown key code {code} (add it to Gw2KeyCodes)");
            return null;
        }

        return new Chord(modifiers, key);
    }
}

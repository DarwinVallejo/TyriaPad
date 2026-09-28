using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;

namespace TyriaPad.Core.Tests;

public class ProfileParserTests
{
    [Fact]
    public void Parses_ShorthandAndGestureBindings()
    {
        Profile profile = ProfileParser.Parse("""
            {
              "name": "test",
              "contexts": {
                "combat": {
                  "leftStick": "move", "rightStick": "camera",
                  "layers": {
                    "base": {
                      "RB": "1",
                      "R3": { "tap": "Tab", "hold": "Ctrl+T", "double": "T" },
                      "Y": { "tap": "Alt+click:left" },
                      "Menu": { "hold": "actionCamera" }
                    },
                    "LT+RT": { "X": "F5" }
                  }
                }
              }
            }
            """);

        ContextLayout combat = profile.Contexts[GameContext.Combat];
        Assert.Equal(["base", "LT+RT"], combat.Layers.Select(static l => l.Name));

        ButtonBinding rb = combat.Base.Bindings[GamepadButtons.RightBumper];
        Assert.Equal(new ChordAction(Key.D1), rb.Press);

        ButtonBinding r3 = combat.Base.Bindings[GamepadButtons.RightStick];
        Assert.Null(r3.Press);
        Assert.Equal(new ChordAction(Key.Tab), r3.Tap);
        Assert.Equal(new ChordAction(new Chord(KeyModifiers.Ctrl, Key.T)), r3.Hold);
        Assert.Equal(new ChordAction(Key.T), r3.Double);

        // Only tap = held while pressed (no delay).
        ButtonBinding y = combat.Base.Bindings[GamepadButtons.Y];
        Assert.Equal(new ChordAction(new Chord(KeyModifiers.Alt, MouseButton.Left)), y.Press);

        Assert.Equal(ActionCameraToggleAction.Instance, combat.Base.Bindings[GamepadButtons.Menu].Hold);
        Assert.Equal(GamepadButtons.LeftTrigger | GamepadButtons.RightTrigger, combat.Layers[1].Modifiers);
    }

    [Fact]
    public void Inherits_MergesLayersAndSticks()
    {
        Profile profile = ProfileParser.Parse("""
            {
              "contexts": {
                "combat": {
                  "leftStick": "move", "rightStick": "camera",
                  "layers": { "base": { "X": "N", "A": "Space" }, "LB": { "X": "7" } }
                },
                "mount": { "inherits": "combat", "layers": { "base": { "X": "C" }, "LB": { "rightStick": "none", "Y": "8" } } }
              }
            }
            """);

        ContextLayout mount = profile.Contexts[GameContext.Mount];
        Assert.Equal(StickRole.Move, mount.LeftStick);
        Assert.Equal(new ChordAction(Key.C), mount.Base.Bindings[GamepadButtons.X].Press);
        Assert.Equal(new ChordAction(Key.Space), mount.Base.Bindings[GamepadButtons.A].Press);

        Layer lb = mount.Layers.Single(static l => l.Name == "LB");
        Assert.Equal(new ChordAction(Key.D7), lb.Bindings[GamepadButtons.X].Press);
        Assert.Equal(new ChordAction(Key.D8), lb.Bindings[GamepadButtons.Y].Press);
        Assert.Equal(StickRole.None, lb.RightStick);

        // The parent doesn't change.
        Assert.Equal(new ChordAction(Key.N), profile.Contexts[GameContext.Combat].Base.Bindings[GamepadButtons.X].Press);
    }

    [Fact]
    public void MissingContext_FallsBackToCombat()
    {
        Profile profile = ProfileParser.Parse("""{ "contexts": { "combat": { "layers": { "base": { "A": "Space" } } } } }""");

        Assert.False(profile.Has(GameContext.Pointer));
        Assert.Same(profile.Contexts[GameContext.Combat], profile.Resolve(GameContext.Pointer));
    }

    [Fact]
    public void Radials_ParseWithStickAndItems()
    {
        Profile profile = ProfileParser.Parse("""
            {
              "contexts": { "combat": { "layers": { "base": { "L3": { "tap": "6", "hold": "radial:mounts" } } } } },
              "radials": { "mounts": { "stick": "right", "items": [ { "label": "Raptor", "action": "Shift+R" }, { "action": "Shift+G" } ] } }
            }
            """);

        RadialMenu mounts = profile.Radials["mounts"];
        Assert.Equal(StickSide.Right, mounts.Stick);
        Assert.Equal("Raptor", mounts.Items[0].Label);
        Assert.Equal("Shift+G", mounts.Items[1].Label);
        Assert.Equal(new RadialAction("mounts"), profile.Contexts[GameContext.Combat].Base.Bindings[GamepadButtons.LeftStick].Hold);
    }

    [Theory]
    [InlineData("""{ "contexts": { "pointer": { "layers": {} } } }""", "the \"combat\" context is missing")]
    [InlineData("""{ "contexts": { "combat": { "layers": { "base": { "RB": "Foo" } } } } }""", "contexts.combat.layers.base.RB: 'Foo': unknown key 'Foo'")]
    [InlineData("""{ "contexts": { "combat": { "layers": { "base": { "Z1": "1" } } } } }""", "contexts.combat.layers.base.Z1: unknown button")]
    [InlineData("""{ "contexts": { "combat": { "layers": { "LB": { "LB": "1" } } } } }""", "LB is a modifier of this layer")]
    [InlineData("""{ "contexts": { "combat": { "layers": { "Foo": { "A": "1" } } } } }""", "contexts.combat.layers.Foo: a layer name is")]
    [InlineData("""{ "contexts": { "combat": { "layers": { "base": { "A": { "press": "1", "tap": "2" } } } } } }""", "\"press\" doesn't combine")]
    [InlineData("""{ "contexts": { "combat": { "layers": { "base": { "A": { "hold": "radial:nope" } } } } } }""", "radial \"nope\" does not exist")]
    [InlineData("""{ "contexts": { "combat": { "layers": { "base": { "A": { "tap": "radial:x" } } } } }, "radials": { "x": { "stick": "left", "items": [ { "action": "1" }, { "action": "2" } ] } } }""", "can only be opened with \"hold\"")]
    [InlineData("""{ "contexts": { "combat": { "inherits": "combat", "layers": {} } } }""", "circular inheritance")]
    [InlineData("""{ "contexts": { "combat": { "inherits": "nope", "layers": {} } } }""", "context \"nope\" does not exist")]
    [InlineData("""{ "contexts": { "combat": { "leftStick": "wasd", "layers": {} } } }""", "contexts.combat.leftStick: must be")]
    [InlineData("""{ "contexts": { "combat": { "layers": { "base": { "A": "wheel:sideways" } } } } }""", "wheel:up or wheel:down")]
    [InlineData("""{ "contexts": { "combat": { "layers": { "base": { "A": "Foo+R" } } } } }""", "'Foo' is not a modifier")]
    [InlineData("""{ "radials": { "m": { "stick": "up", "items": [] } }, "contexts": { "combat": { "layers": {} } } }""", "radials.m.stick")]
    [InlineData("""{ "contexts": { "combat": { "layers": { "base": { "A": 5 } } } } }""", "must be a string")]
    [InlineData("""{ not json }""", "invalid JSON")]
    public void Errors_NameThePath(string json, string expected)
    {
        var ex = Assert.Throws<ConfigException>(() => ProfileParser.Parse(json));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Errors_AreAllReportedTogether()
    {
        var ex = Assert.Throws<ConfigException>(() => ProfileParser.Parse("""
            { "contexts": { "combat": { "layers": { "base": { "A": "Foo", "B": "Bar" } } } } }
            """));

        Assert.Contains("'Foo'", ex.Message);
        Assert.Contains("'Bar'", ex.Message);
    }

    [Theory]
    [InlineData("1", KeyModifiers.None, Key.D1)]
    [InlineData("shift+r", KeyModifiers.Shift, Key.R)]
    [InlineData("Ctrl + Alt + Delete", KeyModifiers.Ctrl | KeyModifiers.Alt, Key.Delete)]
    [InlineData("`", KeyModifiers.None, Key.Grave)]
    [InlineData("Shift+=", KeyModifiers.Shift, Key.Equals)]
    [InlineData("Esc", KeyModifiers.None, Key.Escape)]
    [InlineData("Shift", KeyModifiers.None, Key.LeftShift)]
    public void ChordSyntax(string text, KeyModifiers modifiers, Key key)
    {
        Assert.True(ActionNames.TryParseChord(text, out Chord chord, out string? error), error);
        Assert.Equal(new Chord(modifiers, key), chord);
    }

    [Theory]
    [InlineData("click:left", KeyModifiers.None, MouseButton.Left)]
    [InlineData("Shift+click:Right", KeyModifiers.Shift, MouseButton.Right)]
    public void ClickSyntax(string text, KeyModifiers modifiers, MouseButton button)
    {
        Assert.True(ActionNames.TryParseChord(text, out Chord chord, out string? error), error);
        Assert.Equal(new Chord(modifiers, button), chord);
    }
}

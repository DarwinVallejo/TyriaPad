using TyriaPad.Core.Config;
using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;

namespace TyriaPad.Core.Tests;

public class JsonTextEditorTests
{
    private const string Sample = """
        {
          // header comment
          "profile": "blaggletoad",
          "camera": { "speed": 1400, "exponent": 2 },
          "overlay": {
            "enabled": true,
            "radialRadius": 130       // radius of the radials
          }
        }
        """;

    [Fact]
    public void Set_ReplacesOnlyTheValue_AndKeepsComments()
    {
        string result = JsonTextEditor.Set(Sample, "camera.speed", 2000f);

        Assert.Equal(Sample.Replace("\"speed\": 1400", "\"speed\": 2000"), result);
    }

    [Fact]
    public void Set_IsCaseInsensitive_AndWritesStringsAndBools()
    {
        string result = JsonTextEditor.Set(Sample, "Overlay.Enabled", false);
        result = JsonTextEditor.Set(result, "profile", "mine");

        TyriaPadSettings settings = ConfigStore.ParseSettings(result, "test");
        Assert.False(settings.Overlay.Enabled);
        Assert.Equal("mine", settings.Profile);
        Assert.Contains("// radius of the radials", result);
    }

    [Fact]
    public void Set_AddsMissingProperty_AfterTheLastMember_BeforeItsComment()
    {
        string result = JsonTextEditor.Set(Sample, "overlay.opacity", 0.5f);

        Assert.Contains("\"radialRadius\": 130,       // radius of the radials", result);
        Assert.Contains("    \"opacity\": 0.5\n  }", result.Replace("\r\n", "\n"));
        Assert.Equal(0.5f, ConfigStore.ParseSettings(result, "test").Overlay.Opacity);
    }

    [Fact]
    public void Set_AddsMissingSection_Nested()
    {
        string result = JsonTextEditor.Set(Sample, "gestures.holdMs", 300);

        Assert.Contains("\"gestures\": { \"holdMs\": 300 }", result);
        Assert.Equal(300, ConfigStore.ParseSettings(result, "test").Gestures.HoldMs);
    }

    [Fact]
    public void Set_InEmptyObject_And_WithTrailingComma()
    {
        Assert.Equal("{ \"exitWithGame\": true }", JsonTextEditor.Set("{}", "exitWithGame", true));

        string trailing = "{\n  \"profile\": \"a\",\n}";
        string result = JsonTextEditor.Set(trailing, "exitWithGame", true);
        Assert.Equal("{\n  \"profile\": \"a\",\n  \"exitWithGame\": true\n}", result);
    }

    [Fact]
    public void Set_KeepsBom_AndEnumsAreCamelCase()
    {
        string result = JsonTextEditor.Set("﻿{ \"overlay\": { \"indicator\": \"topRight\" } }", "overlay.indicator", IndicatorCorner.BottomLeft);

        Assert.Equal("﻿{ \"overlay\": { \"indicator\": \"bottomLeft\" } }", result);
    }

    [Fact]
    public void Set_ThroughAValueThatIsNotAnObject_Fails()
    {
        Assert.Throws<FormatException>(() => JsonTextEditor.Set(Sample, "profile.name", "x"));
    }

    [Fact]
    public void SettingsWriter_OnTheEmbeddedConfig_ChangesOnlyWhatDiffers()
    {
        string json = Defaults.ConfigJson;
        TyriaPadSettings before = ConfigStore.ParseSettings(json, "test");
        TyriaPadSettings after = before with
        {
            ExitWithGame = true,
            Camera = before.Camera with { Speed = 1800f },
            Overlay = before.Overlay with { Indicator = IndicatorCorner.None, Opacity = 0.75f },
        };

        string result = SettingsWriter.Apply(json, before, after);

        Assert.Equal(after, ConfigStore.ParseSettings(result, "test"));
        Assert.Equal(3 + 1, SettingsWriter.Diff(before, after, string.Empty).Count());
        Assert.Contains("// TyriaPad settings", result);
        Assert.Equal(json.Split('\n').Length, result.Split('\n').Length);
    }
}

public sealed class ConfigStoreSaveTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TyriaPad-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void SaveProfileAndSettings_AreLoadedBack_AndKeepTheConfigComments()
    {
        using var store = new ConfigStore(_directory);
        store.Start();
        ProfileDraft draft = ProfileDraft.FromJson(store.ReadProfile(Defaults.ProfileName));
        draft.Name = "Mine";
        draft.FindContext("combat")!.GetOrAddLayer(GamepadButtons.None).Bindings[GamepadButtons.M2] = new BindingDraft { Press = "J" };

        store.SaveProfile("mine", draft.ToJson());
        store.SaveSettings(store.Current.Settings with { Profile = "mine", ExitWithGame = true });
        store.Reload();

        Assert.Null(store.LastError);
        Assert.Equal("Mine", store.Current.Profile.Name);
        Assert.True(store.Current.Settings.ExitWithGame);
        Assert.NotNull(store.Current.Profile.Contexts[GameContext.Combat].Base.Find(GamepadButtons.M2));
        Assert.Equal(["blaggletoad", "mine"], store.ListProfiles());
        Assert.Contains("// TyriaPad settings", File.ReadAllText(store.ConfigPath));
        Assert.Equal(Defaults.ProfileJson, File.ReadAllText(store.ProfilePath(Defaults.ProfileName)));
    }

    [Fact]
    public void SaveSettings_RejectsInvalidValues_WithoutTouchingTheFile()
    {
        using var store = new ConfigStore(_directory);
        store.Start();
        string before = File.ReadAllText(store.ConfigPath);

        Assert.Throws<ConfigException>(() => store.SaveSettings(store.Current.Settings with { ActionCameraKey = "DoesNotExist" }));
        Assert.Equal(before, File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void SaveSettings_DoesNotOverwriteAConfigWithErrors()
    {
        using var store = new ConfigStore(_directory);
        store.Start();
        File.WriteAllText(store.ConfigPath, "{ \"camera\": { \"speed\": -5 } } // mine");

        var ex = Assert.Throws<ConfigException>(() => store.SaveSettings(store.Current.Settings with { ExitWithGame = true }));

        Assert.Contains("is not overwritten", ex.Message);
        Assert.Equal("{ \"camera\": { \"speed\": -5 } } // mine", File.ReadAllText(store.ConfigPath));
    }
}

public class ProfileDraftTests
{
    [Fact]
    public void DefaultProfile_RoundTrips_ToTheSameProfile()
    {
        Profile original = ProfileParser.Parse(Defaults.ProfileJson);
        ProfileDraft draft = ProfileDraft.FromJson(Defaults.ProfileJson);

        Profile again = draft.Validate();

        Assert.Equal(original.Name, again.Name);
        Assert.Equal(original.Contexts.Keys.Order(), again.Contexts.Keys.Order());
        foreach ((GameContext context, ContextLayout layout) in original.Contexts)
        {
            ContextLayout other = again.Contexts[context];
            Assert.Equal(layout.LeftStick, other.LeftStick);
            Assert.Equal(layout.RightStick, other.RightStick);
            Assert.Equal(layout.Layers.Select(static l => l.Modifiers), other.Layers.Select(static l => l.Modifiers));
            foreach (Layer layer in layout.Layers)
            {
                Layer same = other.Layers.Single(l => l.Modifiers == layer.Modifiers);
                Assert.Equal(layer.LeftStick, same.LeftStick);
                Assert.Equal(layer.Bindings.OrderBy(static b => b.Key), same.Bindings.OrderBy(static b => b.Key));
            }
        }

        Assert.Equal(original.Radials.Keys.Order(), again.Radials.Keys.Order());
        foreach ((string name, RadialMenu radial) in original.Radials)
        {
            Assert.Equal(radial.Stick, again.Radials[name].Stick);
            Assert.Equal(radial.Items, again.Radials[name].Items);
        }
    }

    [Fact]
    public void ToJson_KeepsInheritance_AndIsStable()
    {
        ProfileDraft draft = ProfileDraft.FromJson(Defaults.ProfileJson);
        string json = draft.ToJson();

        Assert.Contains("\"inherits\": \"combat\"", json);
        Assert.Contains("\"L3\": { \"tap\": \"6\", \"hold\": \"radial:mounts\" }", json);
        Assert.Contains("{ \"label\": \"Skimmer\", \"action\": \"Shift+C\" }", json);
        Assert.Equal(json, ProfileDraft.FromJson(json).ToJson());
    }

    [Fact]
    public void Resolve_FollowsInheritance()
    {
        ProfileDraft draft = ProfileDraft.FromJson(Defaults.ProfileJson);
        ContextDraft mount = draft.FindContext("mount")!;

        (BindingDraft own, ContextDraft ownFrom) = draft.Resolve(mount, GamepadButtons.None, GamepadButtons.X)!.Value;
        (BindingDraft inherited, ContextDraft inheritedFrom) = draft.Resolve(mount, GamepadButtons.LeftBumper, GamepadButtons.X)!.Value;

        Assert.Equal("C", own.Press);
        Assert.Same(mount, ownFrom);
        Assert.Equal("7", inherited.Press);
        Assert.Equal("combat", inheritedFrom.Name);
        Assert.Equal("move", draft.ResolveStick(mount, GamepadButtons.None, StickSide.Left));
        Assert.Contains(GamepadButtons.LeftTrigger | GamepadButtons.RightTrigger, draft.EffectiveLayers(mount));
    }

    [Fact]
    public void OpensLayer_NeedsAllTheModifiersOfTheLayer()
    {
        ProfileDraft draft = ProfileDraft.FromJson(Defaults.ProfileJson);
        ContextDraft combat = draft.FindContext("combat")!;
        const GamepadButtons LtRt = GamepadButtons.LeftTrigger | GamepadButtons.RightTrigger;

        Assert.Equal(GamepadButtons.LeftBumper, draft.OpensLayer(combat, GamepadButtons.None, GamepadButtons.LeftBumper));
        Assert.Equal(GamepadButtons.None, draft.OpensLayer(combat, GamepadButtons.None, GamepadButtons.RightTrigger));
        Assert.Equal(LtRt, draft.OpensLayer(combat, GamepadButtons.LeftTrigger, GamepadButtons.RightTrigger));
        Assert.Equal(GamepadButtons.None, draft.OpensLayer(combat, GamepadButtons.LeftBumper, GamepadButtons.LeftTrigger));
    }

    [Fact]
    public void Edits_AreValidated_ByTheParser()
    {
        ProfileDraft draft = ProfileDraft.FromJson(Defaults.ProfileJson);
        ContextDraft combat = draft.FindContext("combat")!;
        combat.GetOrAddLayer(GamepadButtons.RightBumper).Bindings[GamepadButtons.A] = new BindingDraft { Tap = "radial:mounts", Hold = "J" };

        var ex = Assert.Throws<ConfigException>(() => draft.Validate());

        Assert.Contains("contexts.combat.layers.RB.A.tap", ex.Message);
    }

    [Fact]
    public void Edits_ChangeTheMapping()
    {
        ProfileDraft draft = ProfileDraft.FromJson(Defaults.ProfileJson);
        ContextDraft combat = draft.FindContext("combat")!;
        combat.GetOrAddLayer(GamepadButtons.None).Bindings[GamepadButtons.M2] = new BindingDraft { Press = "Shift+J" };
        draft.FindRadial("mounts")!.Items.RemoveAt(0);

        Profile profile = draft.Validate();

        Assert.Equal("Shift+J", ActionNames.Format(profile.Contexts[GameContext.Combat].Base.Find(GamepadButtons.M2)!.Press!));
        Assert.Equal("Springer", profile.Radials["mounts"].Items[0].Label);
        Assert.True(draft.IsRadialUsed("mounts"));
        Assert.False(draft.IsRadialUsed("other"));
    }

    [Fact]
    public void RenameRadial_UpdatesTheButtonsThatOpenIt()
    {
        ProfileDraft draft = ProfileDraft.FromJson(Defaults.ProfileJson);

        draft.RenameRadial(draft.FindRadial("mounts")!, "rides");
        Profile profile = draft.Validate();

        Assert.False(draft.IsRadialUsed("mounts"));
        Assert.Equal(new RadialAction("rides"), profile.Contexts[GameContext.Combat].Base.Find(GamepadButtons.M1)!.Hold);
        Assert.Equal(new RadialAction("rides"), profile.Contexts[GameContext.Combat].Base.Find(GamepadButtons.LeftStick)!.Hold);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Shift+R")]
    [InlineData("Ctrl+Alt+T")]
    [InlineData(",")]
    [InlineData("`")]
    [InlineData("Esc")]
    [InlineData("Space")]
    [InlineData("F12")]
    [InlineData("Alt+click:left")]
    [InlineData("click:middle")]
    [InlineData("wheel:down")]
    [InlineData("actionCamera")]
    [InlineData("mode:toggle")]
    [InlineData("radial:mounts")]
    [InlineData("RightShift")]
    public void Format_IsTheInverseOfParse(string text)
    {
        Assert.True(ActionNames.TryParse(text, out BindingAction? action, out _));

        Assert.Equal(text, ActionNames.Format(action!));
    }

    [Fact]
    public void EditorKeys_AreAllNamedAndUnique()
    {
        Assert.Equal(KeyNames.EditorKeys.Count, KeyNames.EditorKeys.Distinct().Count());
        foreach (Key key in KeyNames.EditorKeys)
        {
            Assert.True(KeyNames.TryParseKey(KeyNames.ProfileName(key), out Key parsed), key.ToString());
            Assert.Equal(key, parsed);
        }
    }
}

using TyriaPad.Core.Config;
using TyriaPad.Core.Input;
using TyriaPad.Core.Keybinds;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;
using TyriaPad.Core.Overlay;

namespace TyriaPad.Core.Tests;

public class Gw2KeyCodesTests
{
    [Theory]
    [InlineData(5, Key.CapsLock)]
    [InlineData(6, Key.Comma)]
    [InlineData(12, Key.Period)]
    [InlineData(14, Key.Semicolon)]
    [InlineData(19, Key.Delete)]
    [InlineData(23, Key.End)]
    [InlineData(24, Key.Home)]
    [InlineData(25, Key.Insert)]
    [InlineData(17, Key.Grave)]
    [InlineData(32, Key.F1)]
    [InlineData(43, Key.F12)]
    [InlineData('0', Key.D0)]
    [InlineData('7', Key.D7)]
    [InlineData('A', Key.A)]
    [InlineData('Z', Key.Z)]
    public void KnownCodes_MapToScancodes_AndBack(int code, Key expected)
    {
        Assert.True(Gw2KeyCodes.TryToKey(code, out Key key));
        Assert.Equal(expected, key);
        Assert.True(Gw2KeyCodes.TryFromKey(key, out int back));
        Assert.Equal(code, back);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(44)]
    [InlineData(-1)]
    [InlineData(200)]
    public void UnknownCodes_AreRejected(int code)
        => Assert.False(Gw2KeyCodes.TryToKey(code, out _));
}

public class InputBindsParserTests
{
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "samples", "InputBinds-blaggletoad.xml");

    [Fact]
    public void ReferenceSample_ParsesEveryAction()
    {
        InputBindsFile file = InputBindsParser.Parse(File.ReadAllText(SamplePath));

        Assert.Empty(file.Warnings);
        Assert.Equal(25, file.Overrides.Count);
        Assert.Equal(new Gw2Binding(Key.Period, null), file.Overrides[Gw2Action.Walk]);
        Assert.Equal(new Gw2Binding(new Chord(KeyModifiers.Ctrl, Key.L), null), file.Overrides[Gw2Action.AboutFace]);
        Assert.Equal(new Gw2Binding(new Chord(KeyModifiers.Ctrl | KeyModifiers.Alt, Key.F), null), file.Overrides[Gw2Action.PvpPanel]);
        Assert.Equal(new Gw2Binding(Key.CapsLock, new Chord(KeyModifiers.Shift, Key.E)), file.Overrides[Gw2Action.Fishing]);
        Assert.Equal(new Gw2Binding(Key.Insert, null), file.Overrides[Gw2Action.BuildTemplate1]);
        Assert.Equal("Walk", file.Names[Gw2Action.Walk]);
    }

    [Fact]
    public void MouseAndUnknownCodes_AreReported_NotGuessed()
    {
        const string xml = """
            <InputBindings>
                <action name="Dodge" id="6" device="Mouse" button="4"/>
                <action name="Something New" id="999" device="Keyboard" button="150"/>
                <action name="Jump" id="9" device="None"/>
            </InputBindings>
            """;

        InputBindsFile file = InputBindsParser.Parse(xml);

        Assert.True(file.Overrides[Gw2Action.Dodge].IsUnbound);
        Assert.True(file.Overrides[(Gw2Action)999].IsUnbound);
        Assert.True(file.Overrides[Gw2Action.Jump].IsUnbound);
        Assert.Equal(2, file.Warnings.Count);
        Assert.Contains(file.Warnings, static w => w.Contains("Mouse"));
        Assert.Contains(file.Warnings, static w => w.Contains("Something New") && w.Contains("150"));
    }

    [Theory]
    [InlineData("<InputBindings>")]
    [InlineData("<Other/>")]
    public void InvalidFiles_Throw(string xml)
        => Assert.Throws<FormatException>(() => InputBindsParser.Parse(xml));
}

public class Gw2KeybindsTests
{
    private static Gw2Keybinds FromXml(string actions)
        => Gw2Keybinds.FromFile(InputBindsParser.Parse($"<InputBindings>{actions}</InputBindings>"), "test.xml");

    [Fact]
    public void Defaults_CoverTheSkillBar()
    {
        Gw2Keybinds binds = Gw2Keybinds.Defaults;

        Assert.Equal([Gw2Action.Weapon1], binds.ActionsFor(Key.D1));
        Assert.Equal([Gw2Action.Elite], binds.ActionsFor(Key.D0));
        Assert.Equal([Gw2Action.Profession1], binds.ActionsFor(Key.F1));
        Assert.Contains(Gw2Action.Jump, binds.ActionsFor(Key.Space));
        Assert.Contains(Gw2Action.SwimUp, binds.ActionsFor(Key.Space));
        Assert.Equal(SlotKeys.Default.Skills.Select(static s => s.ToArray()), binds.SlotKeys.Skills.Select(static s => s.ToArray()));
    }

    [Fact]
    public void Override_TakesTheKeyAwayFromTheDefaultAction()
    {
        // Mail on Z: GW2 takes it away from "Stow/Draw Weapons".
        Gw2Keybinds binds = FromXml("""<action name="Mail Dialog" id="71" device="Keyboard" button="90"/>""");

        Assert.Equal([Gw2Action.Mail], binds.ActionsFor(Key.Z));
        Assert.True(binds.BindingOf(Gw2Action.StowWeapons).IsUnbound);
        Assert.Equal("test.xml", binds.Source);
    }

    [Fact]
    public void UnknownIds_AreNamedFromTheXml()
    {
        Gw2Keybinds binds = FromXml("""<action name="Some Future Action" id="999" device="Keyboard" button="75"/>""");

        Assert.Equal([(Gw2Action)999], binds.ActionsFor(Key.K));
        Assert.Equal("Some Future Action", binds.NameOf((Gw2Action)999));
    }
}

public class KeybindSlotHintsTests
{
    private static readonly ContextLayout Combat = Defaults.Load().Profile.Resolve(GameContext.Combat);

    private static Gw2Keybinds FromXml(string actions)
        => Gw2Keybinds.FromFile(InputBindsParser.Parse($"<InputBindings>{actions}</InputBindings>"), null);

    [Fact]
    public void SwappingKeysInGw2_MovesTheGlyphs()
    {
        // Skill 1 on key 2 and skill 2 on key 1: RB (which sends 1) moves to slot 2.
        Gw2Keybinds binds = FromXml("""
            <action name="Weapon Skill 1" id="18" device="Keyboard" button="50"/>
            <action name="Weapon Skill 2" id="19" device="Keyboard" button="49"/>
            """);

        IReadOnlyList<SlotHint> hints = SlotHints.Skills(Combat, Combat.Base, binds.SlotKeys);

        Assert.Equal("RT", hints[0].Primary!.ToString());
        Assert.Equal("RB", hints[1].Primary!.ToString());
    }

    [Fact]
    public void SlotOnAModifiedKey_OnlyMatchesTheSameChord()
    {
        // Elite on Shift+0: the profile sends plain "0", so the slot is left without a button.
        Gw2Keybinds binds = FromXml("""<action name="Elite Skill" id="27" device="Keyboard" button="48" mod="1"/>""");

        IReadOnlyList<SlotHint> hints = SlotHints.Skills(Combat, Combat.Base, binds.SlotKeys);

        Assert.Null(hints[9].Primary);
        Assert.Equal("LB+X", hints[6].Primary!.ToString());
    }

    [Fact]
    public void SecondaryBind_AlsoResolves()
    {
        // Skill 7 on U, with 7 as secondary: LB+X still shows up.
        Gw2Keybinds binds = FromXml("""<action name="Utility Skill 1" id="24" device="Keyboard" button="85" device2="Keyboard" button2="55"/>""");

        IReadOnlyList<SlotHint> hints = SlotHints.Skills(Combat, Combat.Base, binds.SlotKeys);

        Assert.Equal("LB+X", hints[6].Primary!.ToString());
    }

    [Fact]
    public void ProfessionSlot8_KeepsF8()
    {
        IReadOnlyList<SlotHint> hints = SlotHints.Profession(Combat, Combat.Base, 8, Gw2Keybinds.Defaults.SlotKeys);

        Assert.Equal(8, hints.Count);
        Assert.Equal("LT+Left", hints[7].Primary!.ToString());
    }
}

public class KeybindCheckTests
{
    private static readonly LoadedConfig Config = Defaults.Load();
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "samples", "InputBinds-blaggletoad.xml");

    private static IReadOnlyList<string> Check(Gw2Keybinds binds, TyriaPadSettings? settings = null)
        => KeybindCheck.Analyze(Config.Profile, settings ?? Config.Settings, binds);

    [Fact]
    public void ReferenceSample_OnlyMissesStrafeAndF8()
    {
        Gw2Keybinds binds = Gw2Keybinds.FromFile(InputBindsParser.Parse(File.ReadAllText(SamplePath)), SamplePath);

        IReadOnlyList<string> issues = Check(binds);

        // The reference XML leaves A/D turning (the post recommends strafing) and GW2 has no F8.
        Assert.Equal(3, issues.Count);
        Assert.Contains(issues, static i => i.StartsWith("Left stick left") && i.Contains("Turn Left"));
        Assert.Contains(issues, static i => i.StartsWith("Left stick right"));
        Assert.Contains(issues, static i => i.StartsWith("F8 has no action"));
    }

    [Fact]
    public void DefaultKeybinds_ReportTheKeysTheLayoutNeedsToChange()
    {
        IReadOnlyList<string> issues = Check(Gw2Keybinds.Defaults);

        // Without XML, among others, Action Camera, the mounts and Walk are missing.
        Assert.Contains(issues, static i => i.Contains("Action Camera") && i.Contains("has no key"));
        Assert.Contains(issues, static i => i.StartsWith("Shift+R has no action") && i.Contains("radial mounts (Raptor)"));
        Assert.Contains(issues, static i => i.StartsWith(". has no action") && i.Contains("combat and mount View (hold)"));
        Assert.DoesNotContain(issues, static i => i.StartsWith("Esc "));
    }

    [Fact]
    public void ActionCameraOnAnotherKey_IsReported()
    {
        Gw2Keybinds binds = Gw2Keybinds.FromFile(
            InputBindsParser.Parse("""<InputBindings><action name="Toggle Action Camera" id="78" device="Keyboard" button="15"/></InputBindings>"""),
            null);

        IReadOnlyList<string> issues = Check(binds);

        Assert.Contains(issues, static i => i == "actionCameraKey is , but in GW2 Action Camera is on /");
    }

    [Fact]
    public void UnboundSkill_AndSkillWithoutButton_AreReported()
    {
        Gw2Keybinds binds = Gw2Keybinds.FromFile(
            InputBindsParser.Parse("""
                <InputBindings>
                    <action name="Heal" id="23" device="None"/>
                    <action name="Elite" id="27" device="Keyboard" button="85"/>
                </InputBindings>
                """),
            null);

        IReadOnlyList<string> issues = Check(binds);

        Assert.Contains("\"Healing Skill\" has no key in GW2", issues);
        Assert.Contains("\"Elite Skill\" (U) has no button in the profile", issues);
    }
}

public sealed class InputBindsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tyriapad-binds-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string xml, DateTime modified)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, xml);
        File.SetLastWriteTimeUtc(path, modified);
        return path;
    }

    [Fact]
    public void Empty_PicksTheNewestFile()
    {
        Write("old.xml", "<InputBindings/>", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        string newest = Write("new.xml", "<InputBindings/>", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(newest, InputBindsStore.Resolve("", _dir));
    }

    [Fact]
    public void Name_WithOrWithoutExtension_AndNone()
    {
        string path = Write("Controller.xml", "<InputBindings/>", DateTime.UtcNow);

        Assert.Equal(path, InputBindsStore.Resolve("Controller", _dir));
        Assert.Equal(path, InputBindsStore.Resolve("Controller.xml", _dir));
        Assert.Null(InputBindsStore.Resolve("none", _dir));
        Assert.Null(InputBindsStore.Resolve("Other", _dir));
    }

    [Fact]
    public void Load_FallsBackToDefaults()
    {
        Write("broken.xml", "<InputBindings>", DateTime.UtcNow);

        Assert.Same(Gw2Keybinds.Defaults, InputBindsStore.Load("", _dir));
        Assert.Same(Gw2Keybinds.Defaults, InputBindsStore.Load("", Path.Combine(_dir, "missing")));
    }

    [Fact]
    public void Load_ReadsTheFile()
    {
        string path = Write("binds.xml", """<InputBindings><action name="Mail Dialog" id="71" device="Keyboard" button="90"/></InputBindings>""", DateTime.UtcNow);

        Gw2Keybinds binds = InputBindsStore.Load("", _dir);

        Assert.Equal(path, binds.Source);
        Assert.Equal([Gw2Action.Mail], binds.ActionsFor(Key.Z));
    }
}

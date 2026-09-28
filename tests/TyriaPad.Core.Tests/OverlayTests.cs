using System.Text;

using TyriaPad.Core.Config;
using TyriaPad.Core.Game;
using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Overlay;

namespace TyriaPad.Core.Tests;

public class SkillBarLayoutTests
{
    [Fact]
    public void Default_IsCenteredAndAnchoredToTheBottom()
    {
        SkillBarCalibration c = SkillBarLayout.DefaultCalibration(1920, 1080, UiSize.Normal);
        SkillBarLayout layout = SkillBarLayout.FromCalibration(c, 5);

        Assert.Equal(10, layout.Skills.Count);
        Assert.Equal(5, layout.Profession.Count);

        // Symmetric around the screen center and inside the screen.
        Assert.Equal(960f, (layout.Skills[0].Center.X + layout.Skills[9].Center.X) / 2, 0.01f);
        Assert.All(layout.Skills, static s => Assert.InRange(s.Center.Y, 1000f, 1080f));
        float barY = layout.Skills[0].Center.Y;
        Assert.All(layout.Profession, s => Assert.True(s.Center.Y < barY, "F1… goes above the bar"));
    }

    [Fact]
    public void Slots_AreEvenlySpacedWithinEachHalf_AndTheOrbSeparatesTheHalves()
    {
        var c = new SkillBarCalibration(new Point2(100, 500), new Point2(300, 500), new Point2(700, 500), new Point2(90, 430), new Point2(130, 430));
        SkillBarLayout layout = SkillBarLayout.FromCalibration(c, 3);

        float[] xs = layout.Skills.Select(static s => s.Center.X).ToArray();
        Assert.Equal([100f, 150f, 200f, 250f, 300f, 500f, 550f, 600f, 650f, 700f], xs);
        Assert.All(layout.Skills, static s => Assert.Equal(500f, s.Center.Y));
        Assert.Equal(45f, layout.Skills[0].Size, 0.01f); // 0.9 × pitch

        Assert.Equal([90f, 130f, 170f], layout.Profession.Select(static s => s.Center.X).ToArray());
    }

    [Fact]
    public void ProfessionSlots_AreClamped()
    {
        SkillBarCalibration c = SkillBarLayout.DefaultCalibration(1920, 1080, UiSize.Normal);

        Assert.Empty(SkillBarLayout.FromCalibration(c, -1).Profession);
        Assert.Equal(8, SkillBarLayout.FromCalibration(c, 20).Profession.Count);
    }

    [Fact]
    public void LargerUi_MakesTheBarWider()
    {
        SkillBarLayout normal = SkillBarLayout.FromCalibration(SkillBarLayout.DefaultCalibration(1920, 1080, UiSize.Normal), 5);
        SkillBarLayout larger = SkillBarLayout.FromCalibration(SkillBarLayout.DefaultCalibration(1920, 1080, UiSize.Larger), 5);

        Assert.True(larger.Skills[9].Center.X - larger.Skills[0].Center.X > normal.Skills[9].Center.X - normal.Skills[0].Center.X);
        Assert.True(larger.Skills[0].Size > normal.Skills[0].Size);
    }

    [Fact]
    public void Default_MatchesTheAllyXCalibration()
    {
        // Calibration done by hand on the ROG Ally X: 1920x1080, Small interface size, Windows at 150%.
        SkillBarCalibration c = SkillBarLayout.DefaultCalibration(1920, 1080, UiSize.Small, 1.5f);

        AssertNear(new Point2(589.4f, 1023.2f), c.Slot1);
        AssertNear(new Point2(875.3f, 1025.2f), c.Slot5);
        AssertNear(new Point2(1337.6f, 1026.2f), c.Slot10);
        AssertNear(new Point2(568.0f, 922.2f), c.F1);
        AssertNear(new Point2(636.3f, 923.2f), c.F2);

        static void AssertNear(Point2 expected, Point2 actual)
        {
            Assert.InRange(actual.X, expected.X - 6, expected.X + 6);
            Assert.InRange(actual.Y, expected.Y - 6, expected.Y + 6);
        }
    }

    [Fact]
    public void DpiScale_GrowsTheBar()
    {
        SkillBarCalibration at100 = SkillBarLayout.DefaultCalibration(1920, 1080, UiSize.Normal);
        SkillBarCalibration at150 = SkillBarLayout.DefaultCalibration(1920, 1080, UiSize.Normal, 1.5f);

        Assert.Equal((at100.Slot5.X - at100.Slot1.X) * 1.5f, at150.Slot5.X - at150.Slot1.X, 0.01f);
        Assert.Equal(at100.Slot1.X + at100.Slot10.X, at150.Slot1.X + at150.Slot10.X, 0.01f); // still centered
    }

    [Fact]
    public void Key_CombinesResolutionAndUiSize()
    {
        Assert.Equal("1920x1080@normal", SkillBarLayout.KeyFor(1920, 1080, UiSize.Normal));
        Assert.Equal("2560x1440@larger", SkillBarLayout.KeyFor(2560, 1440, UiSize.Larger));
    }
}

public class SlotHintsTests
{
    private static readonly Profile Profile = Defaults.Load().Profile;
    private static readonly ContextLayout Combat = Profile.Resolve(GameContext.Combat);

    private static Layer LayerOf(ContextLayout layout, GamepadButtons modifiers)
        => layout.Layers.Single(l => l.Modifiers == modifiers);

    [Fact]
    public void BaseLayer_ResolvesTheReferenceLayout()
    {
        IReadOnlyList<SlotHint> hints = SlotHints.Skills(Combat, Combat.Base);

        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"], hints.Select(static h => h.Label));
        Assert.Equal("RB", hints[0].Primary!.ToString());
        Assert.Equal("RT", hints[1].Primary!.ToString());
        Assert.Equal("LB+RB", hints[2].Primary!.ToString());
        Assert.Equal("LB+RT", hints[3].Primary!.ToString());
        Assert.Equal("L3", hints[5].Primary!.ToString());
        Assert.Equal("LB+X", hints[6].Primary!.ToString());
        Assert.Equal("LB+A", hints[9].Primary!.ToString());

        Assert.True(hints[0].Primary!.Active);
        Assert.False(hints[2].Primary!.Active);
        Assert.Equal(GestureKind.Tap, hints[5].Primary!.Gesture);
    }

    [Fact]
    public void Slot5_HasOneComboAlthoughTwoLayersMapIt()
    {
        // LB→LT and LT→LB both send 5: same set of buttons.
        IReadOnlyList<SlotHint> hints = SlotHints.Skills(Combat, Combat.Base);

        ButtonCombo combo = Assert.Single(hints[4].Combos);
        Assert.Equal(GamepadButtons.LeftBumper | GamepadButtons.LeftTrigger, combo.All);
    }

    [Fact]
    public void ActiveLayer_MarksItsCombosAndPutsThemFirst()
    {
        Layer lb = LayerOf(Combat, GamepadButtons.LeftBumper);
        IReadOnlyList<SlotHint> hints = SlotHints.Skills(Combat, lb);

        Assert.False(hints[0].Primary!.Active); // base-layer RB no longer sends 1 with LB held
        Assert.True(hints[2].Primary!.Active);
        Assert.True(hints[4].Primary!.Active);

        Layer lt = LayerOf(Combat, GamepadButtons.LeftTrigger);
        hints = SlotHints.Skills(Combat, lt);
        Assert.True(hints[4].Primary!.Active);
    }

    [Fact]
    public void Profession_PrefersTheShortestCombo_AndRespectsTheSlotCount()
    {
        IReadOnlyList<SlotHint> hints = SlotHints.Profession(Combat, Combat.Base, 8);

        Assert.Equal(8, hints.Count);
        Assert.Equal("LT+X", hints[0].Primary!.ToString());
        Assert.Equal("LT+Up", hints[4].Primary!.ToString()); // ahead of LT+RT+X
        Assert.Contains(hints[4].Combos, static c => c.All == (GamepadButtons.LeftTrigger | GamepadButtons.RightTrigger | GamepadButtons.X));

        Layer ltRt = LayerOf(Combat, GamepadButtons.LeftTrigger | GamepadButtons.RightTrigger);
        hints = SlotHints.Profession(Combat, ltRt, 5);
        Assert.Equal(5, hints.Count);
        Assert.Equal("LT+RT+X", hints[4].Primary!.ToString()); // the active one beats the shortest
    }

    [Fact]
    public void PointerContext_HasNoSkillHints()
    {
        ContextLayout pointer = Profile.Resolve(GameContext.Pointer);

        Assert.All(SlotHints.Skills(pointer, pointer.Base), static h => Assert.Null(h.Primary));
        Assert.All(SlotHints.Profession(pointer, pointer.Base, 5), static h => Assert.Null(h.Primary));
    }

    [Fact]
    public void MountContext_InheritsTheCombatSlots()
    {
        ContextLayout mount = Profile.Resolve(GameContext.Mount);
        IReadOnlyList<SlotHint> hints = SlotHints.Skills(mount, mount.Base);

        Assert.Equal("RB", hints[0].Primary!.ToString());
        Assert.Equal("LB+X", hints[6].Primary!.ToString());
    }

    [Fact]
    public void DisplayNames_AreWhatTheUserWrites()
    {
        Assert.Equal("Shift+8", KeyNames.DisplayName(new Core.Output.Chord(Core.Output.KeyModifiers.Shift, Core.Output.Key.D8)));
        Assert.Equal("0", KeyNames.DisplayName(Core.Output.Key.D0));
        Assert.Equal(",", KeyNames.DisplayName(Core.Output.Key.Comma));
        Assert.Equal("F5", KeyNames.DisplayName(Core.Output.Key.F5));
        Assert.Equal("Alt+left click", KeyNames.DisplayName(new Core.Output.Chord(Core.Output.KeyModifiers.Alt, Core.Output.MouseButton.Left)));
    }

    [Fact]
    public void GestureAndSequence_AreDescribed()
    {
        var hold = new ButtonCombo(GamepadButtons.LeftBumper | GamepadButtons.LeftTrigger, GamepadButtons.Y, GestureKind.Hold, Active: false);

        Assert.Equal([GamepadButtons.LeftBumper, GamepadButtons.LeftTrigger, GamepadButtons.Y], hold.Sequence);
        Assert.Equal(3, hold.Count);
        Assert.Equal("LB+LT+Y (hold)", hold.ToString());
    }
}

public sealed class CalibrationStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "TyriaPadTests", Guid.NewGuid().ToString("N"));

    public CalibrationStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SetAndRemove_PersistAcrossInstances()
    {
        var calibration = new SkillBarCalibration(new Point2(1, 2), new Point2(3, 4), new Point2(5, 6), new Point2(7, 8), new Point2(9, 10));
        var store = new CalibrationStore(_dir);
        Assert.Equal(0, store.Count);

        store.Set("1920x1080@normal", calibration);

        var reloaded = new CalibrationStore(_dir);
        Assert.Equal(calibration, reloaded.Get("1920x1080@normal"));
        Assert.Null(reloaded.Get("1920x1080@large"));

        Assert.True(reloaded.Remove("1920x1080@normal"));
        Assert.False(reloaded.Remove("1920x1080@normal"));
        Assert.Null(new CalibrationStore(_dir).Get("1920x1080@normal"));
    }

    [Fact]
    public void Json_IsReadableAndTolerant()
    {
        string json = CalibrationStore.Serialize(new Dictionary<string, SkillBarCalibration>
        {
            ["1920x1080@normal"] = new(new Point2(1, 2), new Point2(3, 4), new Point2(5, 6), new Point2(7, 8), new Point2(9, 10)),
        });

        Assert.Contains("\"slot1\"", json);
        Assert.Contains("\"x\": 1", json);

        // An incomplete entry is ignored without dropping the others.
        Dictionary<string, SkillBarCalibration> entries = CalibrationStore.Deserialize("""
            {
              "1920x1080@normal": { "slot1": {"x":1,"y":2}, "slot5": {"x":3,"y":4}, "slot10": {"x":5,"y":6}, "f1": {"x":7,"y":8}, "f2": {"x":9,"y":10} },
              "broken": { "slot1": {"x":1,"y":2} }
            }
            """);
        Assert.Single(entries);
        Assert.Equal(new Point2(9, 10), entries["1920x1080@normal"].F2);
    }

    [Fact]
    public void BrokenFile_IsIgnored()
    {
        File.WriteAllText(Path.Combine(_dir, CalibrationStore.FileName), "{ this is not JSON");

        var store = new CalibrationStore(_dir);

        Assert.Equal(0, store.Count);
    }
}

public class UiSizeTests
{
    [Theory]
    [InlineData("""{"name":"Kira","profession":4,"uisz":0}""", UiSize.Small)]
    [InlineData("""{"name":"Kira","profession":4,"uisz":1,"fov":0.87}""", UiSize.Normal)]
    [InlineData("""{"uisz":3}""", UiSize.Larger)]
    public void ParseUiSize_ReadsTheIdentityJson(string json, UiSize expected)
    {
        Assert.Equal(expected, MumbleLinkData.ParseUiSize(Identity(json)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"name\":\"a\"")]
    [InlineData("""{"uisz":7}""")]
    [InlineData("""[1,2]""")]
    public void ParseUiSize_ReturnsNullWhenUnusable(string json)
    {
        Assert.Null(MumbleLinkData.ParseUiSize(Identity(json)));
    }

    [Fact]
    public void Scale_GrowsWithTheSize()
    {
        Assert.Equal(1f, UiSize.Normal.Scale());
        Assert.True(UiSize.Small.Scale() < 1f);
        Assert.True(UiSize.Large.Scale() > 1f);
        Assert.True(UiSize.Larger.Scale() > UiSize.Large.Scale());
    }

    private static byte[] Identity(string json)
    {
        var buffer = new byte[MumbleLinkData.IdentityLength];
        Encoding.Unicode.GetBytes(json).CopyTo(buffer, 0);
        return buffer;
    }
}

public class OverlaySettingsTests
{
    [Fact]
    public void Defaults_MatchTheEmbeddedConfig()
    {
        OverlaySettings overlay = Defaults.Load().Settings.Overlay;

        Assert.True(overlay.Enabled);
        Assert.Equal(IndicatorCorner.TopRight, overlay.Indicator);
        Assert.Equal(5, overlay.ProfessionSlots);
    }

    [Theory]
    [InlineData("""{ "overlay": { "scale": 0 } }""", "overlay.scale")]
    [InlineData("""{ "overlay": { "opacity": 1.5 } }""", "overlay.opacity")]
    [InlineData("""{ "overlay": { "professionSlots": 9 } }""", "overlay.professionSlots")]
    [InlineData("""{ "overlay": { "radialRadius": 10 } }""", "overlay.radialRadius")]
    [InlineData("""{ "overlay": { "indicator": "middle" } }""", "JSON")]
    public void InvalidOverlaySettings_AreRejected(string json, string expected)
    {
        var ex = Assert.Throws<ConfigException>(() => ConfigStore.ParseSettings(json, "test"));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Indicator_IsParsedCaseInsensitively()
    {
        TyriaPadSettings settings = ConfigStore.ParseSettings("""{ "overlay": { "indicator": "bottomleft", "enabled": false } }""", "test");

        Assert.Equal(IndicatorCorner.BottomLeft, settings.Overlay.Indicator);
        Assert.False(settings.Overlay.Enabled);
    }
}

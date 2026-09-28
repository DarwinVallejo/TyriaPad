using TyriaPad.Core.Config;
using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;

namespace TyriaPad.Core.Tests;

public class DefaultsTests
{
    [Fact]
    public void EmbeddedConfig_MatchesCodeDefaults()
    {
        LoadedConfig config = Defaults.Load();

        // If this fails, config.json and TyriaPadSettings have drifted apart.
        Assert.Equal(TyriaPadSettings.Default, config.Settings);
        Assert.Equal(Key.Comma, config.Settings.ActionCameraKeyCode);
    }

    [Fact]
    public void EmptyConfig_IsValid_AndEqualsDefaults()
    {
        Assert.Equal(TyriaPadSettings.Default, ConfigStore.ParseSettings("{}", "test"));
    }

    [Fact]
    public void PartialConfig_OverridesOnlyWhatItNames()
    {
        TyriaPadSettings settings = ConfigStore.ParseSettings("""{ "camera": { "speed": 2000 }, "gestures": { "holdMs": 300 } }""", "test");

        Assert.Equal(2000f, settings.Camera.Speed);
        Assert.Equal(2f, settings.Camera.Exponent);
        Assert.Equal(300, settings.Gestures.HoldMs);
        Assert.Equal(250, settings.Gestures.DoubleTapMs);
    }

    [Theory]
    [InlineData("""{ "actionCameraKey": "Foo" }""", "actionCameraKey")]
    [InlineData("""{ "gestures": { "resyncMs": 100 } }""", "resyncMs")]
    [InlineData("""{ "movement": { "pressThreshold": 0.3, "releaseThreshold": 0.5 } }""", "movement")]
    [InlineData("""{ "input": { "leftStickDeadzone": 1.5 } }""", "deadzones")]
    [InlineData("""{ "profile": "" }""", "profile")]
    public void InvalidConfig_IsRejected(string json, string expected)
    {
        var ex = Assert.Throws<ConfigException>(() => ConfigStore.ParseSettings(json, "test"));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void DefaultProfile_CoversTheReferenceLayout()
    {
        Profile profile = Defaults.Load().Profile;

        Assert.True(profile.Has(GameContext.Combat));
        Assert.True(profile.Has(GameContext.Pointer));
        Assert.True(profile.Has(GameContext.Mount));
        Assert.Equal(["mounts", "masteries", "windows"], profile.Radials.Keys);

        ContextLayout combat = profile.Contexts[GameContext.Combat];
        Assert.Equal(["base", "LB", "LT", "LT+RT"], combat.Layers.Select(static l => l.Name));

        // Skills 1-0 and F1-F8 are all assigned.
        var keys = combat.Layers
            .SelectMany(static l => l.Bindings.Values)
            .SelectMany(static b => new[] { b.Press, b.Tap, b.Hold, b.Double })
            .OfType<ChordAction>()
            .Where(static c => c.Chord.Modifiers == KeyModifiers.None && c.Chord.Action.Kind == OutputKind.Key)
            .Select(static c => c.Chord.Action.Key)
            .ToHashSet();
        Key[] expected =
        [
            Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0,
            Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8,
            Key.Space, Key.V, Key.F, Key.N, Key.Tab, Key.Grave,
        ];
        foreach (Key key in expected)
        {
            Assert.Contains(key, keys);
        }

        // The radial's mounts match the keys in section 1 of the reference layout.
        RadialMenu mounts = profile.Radials["mounts"];
        Assert.Equal(9, mounts.Items.Count);
        Assert.All(mounts.Items, static item => Assert.Equal(KeyModifiers.Shift, ((ChordAction)item.Action).Chord.Modifiers));
    }
}

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "TyriaPadTests", Guid.NewGuid().ToString("N"));

    public ConfigStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Start_CreatesDefaultFiles_AndLoadsThem()
    {
        using var store = new ConfigStore(_dir);
        LoadedConfig config = store.Start();

        Assert.True(File.Exists(store.ConfigPath));
        Assert.True(File.Exists(Path.Combine(store.ProfilesDirectory, "blaggletoad.json")));
        Assert.Null(store.LastError);
        Assert.Equal("Blaggletoad v5.0", config.Profile.Name);
        Assert.Equal(TyriaPadSettings.Default, config.Settings);
    }

    [Fact]
    public void Start_KeepsExistingFiles()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "profiles"));
        File.WriteAllText(Path.Combine(_dir, "config.json"), """{ "profile": "mine", "camera": { "speed": 500 } }""");
        File.WriteAllText(Path.Combine(_dir, "profiles", "mine.json"), """{ "name": "Mine", "contexts": { "combat": { "layers": { "base": { "A": "Space" } } } } }""");

        using var store = new ConfigStore(_dir);
        LoadedConfig config = store.Start();

        Assert.Equal("Mine", config.Profile.Name);
        Assert.Equal(500f, config.Settings.Camera.Speed);
    }

    [Fact]
    public void BrokenFile_KeepsPreviousConfig_AndReportsError()
    {
        using var store = new ConfigStore(_dir);
        store.Start();
        int changes = 0;
        store.Changed += _ => changes++;

        File.WriteAllText(store.ConfigPath, """{ "actionCameraKey": "Nope" }""");
        store.Reload();

        Assert.NotNull(store.LastError);
        Assert.Contains("actionCameraKey", store.LastError);
        Assert.Equal(TyriaPadSettings.Default, store.Current.Settings);
        Assert.Equal(0, changes);

        File.WriteAllText(store.ConfigPath, """{ "actionCameraKey": "." }""");
        store.Reload();

        Assert.Null(store.LastError);
        Assert.Equal(Key.Period, store.Current.Settings.ActionCameraKeyCode);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void MissingProfile_IsReported()
    {
        using var store = new ConfigStore(_dir);
        store.Start();
        File.WriteAllText(store.ConfigPath, """{ "profile": "nope" }""");

        store.Reload();

        Assert.Contains("profile \"nope\" does not exist", store.LastError);
    }

    [Fact]
    public async Task EditingTheProfile_ReloadsAutomatically()
    {
        using var store = new ConfigStore(_dir);
        store.Start();
        var reloaded = new TaskCompletionSource<LoadedConfig>();
        store.Changed += config => reloaded.TrySetResult(config);

        string profilePath = Path.Combine(store.ProfilesDirectory, "blaggletoad.json");
        File.WriteAllText(profilePath, """{ "name": "Edited", "contexts": { "combat": { "layers": { "base": { "A": "Space" } } } } }""");

        LoadedConfig config = await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Edited", config.Profile.Name);
    }
}

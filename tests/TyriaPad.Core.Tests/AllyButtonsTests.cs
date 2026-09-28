using TyriaPad.Core.Config;
using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;

namespace TyriaPad.Core.Tests;

/// <summary>M1/M2 through the ASUS HID collection: codes, decoder, wizard and profile.</summary>
public class AllyButtonsTests
{
    private static readonly TimeSpan ReleaseAfter = TimeSpan.FromMilliseconds(150);

    private static byte[] Report(byte code) => [0x5A, code, 0, 0, 0, 0];

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    // --- Codes ---

    [Theory]
    [InlineData("A5", 0xA5, null)]
    [InlineData("a5/00", 0xA5, 0x00)]
    [InlineData("0xEC / 0xED", 0xEC, 0xED)]
    public void Code_Parses(string text, byte press, int? release)
    {
        Assert.True(AllyButtonCode.TryParse(text, out AllyButtonCode code));
        Assert.Equal(new AllyButtonCode(press, (byte?)release), code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00")]
    [InlineData("A5/A5")]
    [InlineData("ZZ")]
    [InlineData("A5/00/01")]
    public void Code_RejectsInvalid(string text) => Assert.False(AllyButtonCode.TryParse(text, out _));

    [Fact]
    public void CodesFile_RoundTrips_AndIgnoresUnknownEntries()
    {
        var codes = new Dictionary<GamepadButtons, AllyButtonCode>
        {
            [GamepadButtons.M1] = new(0xA5, 0x00),
            [GamepadButtons.M2] = new(0xA8),
        };

        Assert.Equal(codes, AllyVendorButtons.Parse(AllyVendorButtons.Serialize(codes)));
        Assert.Equal(
            new Dictionary<GamepadButtons, AllyButtonCode> { [GamepadButtons.M1] = new(0xA5) },
            AllyVendorButtons.Parse("""{ "M1": "A5", "A": "38", "M2": "nope" }"""));
    }

    // --- Decoder ---

    [Fact]
    public void Decoder_WithReleaseCode_HoldsUntilRelease()
    {
        var decoder = new AllyButtonDecoder(new Dictionary<GamepadButtons, AllyButtonCode> { [GamepadButtons.M1] = new(0xA5, 0x00) }, ReleaseAfter);

        Assert.True(decoder.OnReport(Report(0xA5), Ms(0)));
        Assert.Equal(GamepadButtons.M1, decoder.Poll(Ms(1000))); // no time limit: there's a release code

        decoder.OnReport(Report(0x00), Ms(1001));
        Assert.Equal(GamepadButtons.None, decoder.Poll(Ms(1002)));
    }

    [Fact]
    public void Decoder_WithoutReleaseCode_ReleasesWhenReportsStop()
    {
        var decoder = new AllyButtonDecoder(new Dictionary<GamepadButtons, AllyButtonCode> { [GamepadButtons.M2] = new(0xA8) }, ReleaseAfter);

        decoder.OnReport(Report(0xA8), Ms(0));
        Assert.Equal(GamepadButtons.M2, decoder.Poll(Ms(100)));

        // If the firmware repeats the code while held, it stays pressed.
        decoder.OnReport(Report(0xA8), Ms(120));
        Assert.Equal(GamepadButtons.M2, decoder.Poll(Ms(250)));

        Assert.Equal(GamepadButtons.None, decoder.Poll(Ms(300)));
    }

    [Fact]
    public void Decoder_QuickTapBetweenPolls_IsStillSeenOnce()
    {
        var decoder = new AllyButtonDecoder(new Dictionary<GamepadButtons, AllyButtonCode> { [GamepadButtons.M1] = new(0xA5, 0xA6) }, ReleaseAfter);

        decoder.OnReport(Report(0xA5), Ms(0));
        decoder.OnReport(Report(0xA6), Ms(10));

        Assert.Equal(GamepadButtons.M1, decoder.Poll(Ms(50)));
        Assert.Equal(GamepadButtons.None, decoder.Poll(Ms(60)));
    }

    [Fact]
    public void Decoder_IgnoresOtherReportsAndUnknownCodes()
    {
        var decoder = new AllyButtonDecoder(new Dictionary<GamepadButtons, AllyButtonCode> { [GamepadButtons.M1] = new(0xA5) }, ReleaseAfter);

        Assert.False(decoder.OnReport([0x01, 0xA5], Ms(0))); // another report id
        Assert.False(decoder.OnReport(Report(0x38), Ms(0))); // Armoury Crate button or another one
        Assert.Equal(GamepadButtons.None, decoder.Poll(Ms(1)));
    }

    // --- Wizard ---

    [Fact]
    public void Learner_RecordsPressAndReleaseCodes_InOrder()
    {
        var learner = new AllyButtonLearner(AllyVendorButtons.Learnable, Ms(0));

        learner.Feed(Report(0xA5), Ms(100));
        Assert.Equal(LearnStatus.Capturing, learner.Status);
        learner.Feed(Report(0xA5), Ms(200)); // repeat while held
        learner.Feed(Report(0x00), Ms(900));
        Assert.Equal(GamepadButtons.M2, learner.Current);

        // The previous button's late 00 doesn't count for the next one.
        learner.Feed(Report(0x00), Ms(950));
        learner.Feed(Report(0xA8), Ms(1500));
        learner.Tick(Ms(3600)); // no release code arrives: press only

        Assert.Equal(LearnStatus.Done, learner.Status);
        Assert.Equal(
            new Dictionary<GamepadButtons, AllyButtonCode>
            {
                [GamepadButtons.M1] = new(0xA5, 0x00),
                [GamepadButtons.M2] = new(0xA8),
            },
            learner.Learned);
    }

    [Fact]
    public void Learner_IgnoresHeartbeatAndBackgroundCodes()
    {
        // Real case from the log: EC arrived on its own every ~4 s and was learned as M2.
        var learner = new AllyButtonLearner(AllyVendorButtons.Learnable, Ms(0), background: [0x77]);

        learner.Feed(Report(0xEC), Ms(100));
        learner.Feed(Report(0x77), Ms(200));
        Assert.Equal(LearnStatus.Waiting, learner.Status);

        learner.Feed(Report(0xA5), Ms(300));
        learner.Feed(Report(0xEC), Ms(400)); // the heartbeat doesn't count as a release code either
        learner.Feed(Report(0x00), Ms(800));

        Assert.Equal(new AllyButtonCode(0xA5, 0x00), learner.Learned[GamepadButtons.M1]);
    }

    [Fact]
    public void PeriodicDetector_FlagsRegularCodes_NotButtonPresses()
    {
        var detector = new PeriodicCodeDetector();
        foreach (int ms in new[] { 0, 4016, 8020, 12037 })
        {
            detector.Observe(0x42, Ms(ms)); // fixed cadence, like the heartbeat
        }

        foreach (int ms in new[] { 1000, 2300, 6100, 6400 })
        {
            detector.Observe(0xA5, Ms(ms)); // a person's presses
        }

        detector.Observe(0xEC, Ms(11000)); // known: once is enough

        Assert.Equal(new byte[] { 0x42, 0xEC }, detector.Periodic(Ms(13000)).Order());
        Assert.DoesNotContain((byte)0x42, detector.Periodic(Ms(30000))); // stopped arriving
    }

    [Fact]
    public void CodesFile_DropsHeartbeatLearnedByOldVersion()
        => Assert.Equal(
            new Dictionary<GamepadButtons, AllyButtonCode> { [GamepadButtons.M1] = new(0xA5, 0x00) },
            AllyVendorButtons.Parse("""{ "M1": "A5/00", "M2": "EC" }"""));

    [Fact]
    public void Learner_M2WithSameCodeAsM1_StopsAndKeepsM1()
    {
        // Real case: on the Ally X both paddles send 5A A5 and then 5A 00.
        var learner = new AllyButtonLearner(AllyVendorButtons.Learnable, Ms(0));
        learner.Feed(Report(0xA5), Ms(100));
        learner.Feed(Report(0x00), Ms(900));

        learner.Feed(Report(0xA5), Ms(5000));

        Assert.Equal(LearnStatus.SameAsPrevious, learner.Status);
        Assert.Equal(GamepadButtons.M2, learner.Current);
        Assert.Equal(GamepadButtons.M1, learner.SameAs);
        Assert.Equal(new AllyButtonCode(0xA5, 0x00), Assert.Single(learner.Learned).Value);
    }

    [Fact]
    public void DefaultProfile_CurrentIsNotMarkedAsPrevious_AndFingerprintIgnoresLineEndings()
    {
        Assert.False(Defaults.IsPreviousDefaultProfile(Defaults.ProfileJson));
        Assert.Equal(Defaults.Fingerprint("a\nb\n"), Defaults.Fingerprint("﻿a\r\nb\r\n"));
    }

    [Fact]
    public void Learner_TimesOutWithoutReports()
    {
        var learner = new AllyButtonLearner(AllyVendorButtons.Learnable, Ms(0), waitTimeout: Ms(1000));
        var seen = new List<LearnStatus>();
        learner.Progress += l => seen.Add(l.Status);

        learner.Tick(Ms(500));
        learner.Tick(Ms(1000));

        Assert.Equal([LearnStatus.TimedOut], seen);
        Assert.Equal(GamepadButtons.M1, learner.Current);
        Assert.Empty(learner.Learned);
    }

    // --- Input and profile ---

    [Fact]
    public void RawGamepad_ExtraButtons_ReachTheState()
    {
        var raw = new RawGamepad(0x1000, 0, 0, 0, 0, 0, 0, GamepadButtons.M1 | GamepadButtons.A);

        GamepadState state = GamepadState.FromRaw(raw, GamepadButtons.None, InputSettings.Default);

        // Extra only accepts M1/M2: the rest come from XInput.
        Assert.Equal(GamepadButtons.A | GamepadButtons.M1, state.Buttons);
    }

    [Fact]
    public void ProfileNames_IncludeM1AndM2()
    {
        Assert.True(KeyNames.TryParseButton("m1", out GamepadButtons m1));
        Assert.True(KeyNames.TryParseButton("M2", out GamepadButtons m2));
        Assert.Equal(GamepadButtons.M1, m1);
        Assert.Equal(GamepadButtons.M2, m2);
    }

    [Fact]
    public void DefaultProfile_M1Tap_Mounts_M1Hold_OpensMountRadial()
    {
        var h = new MapperHarness();

        h.Tap(GamepadButtons.M1);
        Assert.Equal(["X down", "X up"], h.Events);
        h.Events.Clear();

        h.Hold(GamepadButtons.M1, Ms(300));
        Assert.Equal("mounts", h.Mapper.Radial?.Menu.Name);
        Assert.Empty(h.Events);
    }

    [Fact]
    public void DefaultConfig_EnablesAllyButtons()
    {
        TyriaPadSettings settings = Defaults.Load().Settings;

        Assert.True(settings.AllyButtons.Enabled);
        Assert.Equal(150, settings.AllyButtons.ReleaseAfterMs);
        Assert.Contains("allyButtons.releaseAfterMs: must be greater than 0", (settings with { AllyButtons = new() { ReleaseAfterMs = 0 } }).Validate());
    }
}

public sealed class AllyButtonsDefaultCodesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("TyriaPad-ally-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void WithoutAllyButtonsJson_M1UsesTheAllyXCode()
    {
        using var buttons = new AllyVendorButtons(_directory, new AllyButtonsSettings { Enabled = false });

        Assert.Equal(new AllyButtonCode(0xA5, 0x00), buttons.Codes[GamepadButtons.M1]);
        Assert.False(buttons.Codes.ContainsKey(GamepadButtons.M2));
    }

    [Fact]
    public void LearnedCodes_ReplaceTheDefaults()
    {
        File.WriteAllText(Path.Combine(_directory, AllyVendorButtons.FileName), """{ "M2": "A8" }""");

        using var buttons = new AllyVendorButtons(_directory, new AllyButtonsSettings { Enabled = false });

        Assert.False(buttons.Codes.ContainsKey(GamepadButtons.M1));
        Assert.Equal(new AllyButtonCode(0xA8), buttons.Codes[GamepadButtons.M2]);
    }
}

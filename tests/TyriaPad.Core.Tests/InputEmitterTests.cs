using TyriaPad.Core.Output;

namespace TyriaPad.Core.Tests;

public class InputEmitterTests
{
    private readonly RecordingSink _sink = new();
    private readonly InputEmitter _emitter;

    public InputEmitterTests()
    {
        _emitter = new InputEmitter(_sink);
    }

    [Fact]
    public void SameKeyFromTwoSources_ReleasesOnlyAfterBoth()
    {
        _emitter.Press(Key.M);
        _emitter.Press(Key.M);
        _emitter.Release(Key.M);

        Assert.Equal(["M down"], _sink.Events);

        _emitter.Release(Key.M);

        Assert.Equal(["M down", "M up"], _sink.Events);
    }

    [Fact]
    public void Tap_ReleasesAfterTapDuration()
    {
        _emitter.Tap(Key.Escape, TimeSpan.Zero);
        _emitter.Update(InputEmitter.TapDuration / 2);

        Assert.Equal(["Escape down"], _sink.Events);

        _emitter.Update(InputEmitter.TapDuration);

        Assert.Equal(["Escape down", "Escape up"], _sink.Events);
    }

    [Fact]
    public void ReleaseAll_ReleasesKeysAndMouseButtons()
    {
        _emitter.Press(Key.W);
        _emitter.Press(MouseButton.Left);
        _emitter.Tap(Key.Comma, TimeSpan.Zero);

        _emitter.ReleaseAll();

        Assert.Contains("W up", _sink.Events);
        Assert.Contains("MouseLeft up", _sink.Events);
        Assert.Contains("Comma up", _sink.Events);
        Assert.False(_emitter.IsHeld(Key.W));
    }

    [Fact]
    public void Chord_PressesModifiersFirst_AndReleasesThemLast()
    {
        var chord = new Chord(KeyModifiers.Shift | KeyModifiers.Ctrl, Key.R);
        _emitter.Press(chord);
        _emitter.Release(chord);

        Assert.Equal(["LeftShift down", "LeftCtrl down", "R down", "R up", "LeftCtrl up", "LeftShift up"], _sink.Events);
    }

    [Fact]
    public void TwoChordsSharingShift_KeepShiftUntilBothEnd()
    {
        _emitter.Press(new Chord(KeyModifiers.Shift, Key.R));
        _emitter.Tap(new Chord(KeyModifiers.Shift, Key.G), TimeSpan.Zero);
        _emitter.Update(InputEmitter.TapDuration);

        Assert.Equal(["LeftShift down", "R down", "G down", "G up"], _sink.Events);

        _emitter.Release(new Chord(KeyModifiers.Shift, Key.R));

        Assert.Equal("LeftShift up", _sink.Events[^1]);
    }

    [Fact]
    public void ReleaseAll_ReleasesModifiersLast()
    {
        _emitter.Press(new Chord(KeyModifiers.Alt, MouseButton.Left));

        _emitter.ReleaseAll();

        Assert.Equal(["LeftAlt down", "MouseLeft down", "MouseLeft up", "LeftAlt up"], _sink.Events);
    }

    [Fact]
    public void Wheel_SendsNotches()
    {
        _emitter.Wheel(1);
        _emitter.Wheel(-2);

        Assert.Equal(["Wheel 120", "Wheel -240"], _sink.Events);
    }

    [Fact]
    public void ReleaseOfUnheldAction_DoesNothing()
    {
        _emitter.Release(Key.W);

        Assert.Empty(_sink.Events);
    }
}
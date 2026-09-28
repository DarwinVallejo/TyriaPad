using TyriaPad.Core.Input;

namespace TyriaPad.Core.Tests;

public class GamepadStateTests
{
    private static readonly InputSettings Settings = InputSettings.Default;

    [Fact]
    public void Stick_InsideDeadzone_IsZero()
    {
        Stick stick = Stick.FromRaw(5000, -5000, 0.24f);

        Assert.Equal(default, stick);
    }

    [Fact]
    public void Stick_FullDeflection_HasMagnitudeOne()
    {
        Assert.Equal(1f, Stick.FromRaw(32767, 0, 0.24f).Magnitude, 3);
        Assert.Equal(1f, Stick.FromRaw(0, -32768, 0.24f).Magnitude, 3);
        Assert.Equal(1f, Stick.FromRaw(32767, 32767, 0.24f).Magnitude, 3);
    }

    [Fact]
    public void Stick_JustOutsideDeadzone_StartsNearZero()
    {
        Stick stick = Stick.FromRaw((short)(32767 * 0.25f), 0, 0.24f);

        Assert.InRange(stick.X, 0.001f, 0.05f);
    }

    [Fact]
    public void Trigger_UsesHysteresis()
    {
        var half = new RawGamepad(0, 64, 0, 0, 0, 0, 0); // 0.25: between the release and press thresholds

        Assert.False(GamepadState.FromRaw(half, GamepadButtons.None, Settings).IsDown(GamepadButtons.LeftTrigger));
        Assert.True(GamepadState.FromRaw(half, GamepadButtons.LeftTrigger, Settings).IsDown(GamepadButtons.LeftTrigger));
    }

    [Fact]
    public void Buttons_MapXInputBits()
    {
        var raw = new RawGamepad(0x1000 | 0x0200 | 0x0010, 0, 255, 0, 0, 0, 0);

        GamepadState state = GamepadState.FromRaw(raw, GamepadButtons.None, Settings);

        Assert.Equal(
            GamepadButtons.A | GamepadButtons.RightBumper | GamepadButtons.Menu | GamepadButtons.RightTrigger,
            state.Buttons);
    }

    [Fact]
    public void Frame_ComputesEdges()
    {
        var state = new GamepadState(GamepadButtons.A | GamepadButtons.B, default, default, 0, 0);

        GamepadFrame frame = GamepadFrame.Create(GamepadButtons.B | GamepadButtons.X, state, true, TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(GamepadButtons.A, frame.Pressed);
        Assert.Equal(GamepadButtons.X, frame.Released);
    }
}
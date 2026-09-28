using TyriaPad.Core.Input;

namespace TyriaPad.Core.Tests;

public class HidGamepadDecoderTests
{
    // Like the Ally X IG_ collection: 16-bit axes centered at 0x8000, triggers combined on Z.
    private static readonly HidAxisRange Axis16 = new(0, 65535);
    private static readonly HidGamepadLayout Ally = new(Axis16, Axis16, Axis16, Axis16, Axis16, Rz: null, Hat: new HidAxisRange(1, 8));

    private static RawGamepad Decode(ushort[] buttons, Dictionary<ushort, uint> axes, HidGamepadLayout? layout = null)
        => HidGamepadDecoder.Decode(layout ?? Ally, buttons, usage => axes.TryGetValue(usage, out uint v) ? v : null);

    private static Dictionary<ushort, uint> Centered(params (ushort Usage, uint Value)[] changes)
    {
        var axes = new Dictionary<ushort, uint> { [0x30] = 0x8000, [0x31] = 0x8000, [0x32] = 0x8000, [0x33] = 0x8000, [0x34] = 0x8000, [0x39] = 0 };
        foreach ((ushort usage, uint value) in changes)
        {
            axes[usage] = value;
        }

        return axes;
    }

    [Fact]
    public void AtRest_IsIdle()
    {
        RawGamepad pad = Decode([], Centered());

        Assert.True(FallbackGamepadSource.IsIdle(pad));
        Assert.True(Ally.CombinedTriggers);
    }

    [Fact]
    public void Buttons_FollowTheXInputHidOrder()
    {
        RawGamepad pad = Decode([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], Centered());

        var expected = GamepadButtons.A | GamepadButtons.B | GamepadButtons.X | GamepadButtons.Y | GamepadButtons.LeftBumper
            | GamepadButtons.RightBumper | GamepadButtons.View | GamepadButtons.Menu | GamepadButtons.LeftStick | GamepadButtons.RightStick;
        Assert.Equal((ushort)expected, pad.Buttons);
    }

    [Theory]
    [InlineData(1u, GamepadButtons.DPadUp)]
    [InlineData(2u, GamepadButtons.DPadUp | GamepadButtons.DPadRight)]
    [InlineData(3u, GamepadButtons.DPadRight)]
    [InlineData(5u, GamepadButtons.DPadDown)]
    [InlineData(7u, GamepadButtons.DPadLeft)]
    [InlineData(8u, GamepadButtons.DPadUp | GamepadButtons.DPadLeft)]
    [InlineData(0u, GamepadButtons.None)]
    public void Hat_IsTheDPad(uint hat, GamepadButtons expected)
    {
        Assert.Equal((ushort)expected, Decode([], Centered((0x39, hat))).Buttons);
    }

    [Fact]
    public void Sticks_AreScaled_AndYIsUp()
    {
        RawGamepad pad = Decode([], Centered((0x30, 0), (0x31, 0), (0x33, 65535), (0x34, 65535)));

        Assert.Equal(-32767, pad.LeftX);
        Assert.Equal(32767, pad.LeftY);   // HID up = 0 → XInput up = +
        Assert.Equal(32767, pad.RightX);
        Assert.Equal(-32767, pad.RightY);
    }

    [Fact]
    public void CombinedTriggers_SplitAroundTheCenter_AndCancelOut()
    {
        Assert.Equal((byte)255, Decode([], Centered((0x32, 65535))).LeftTrigger);
        Assert.Equal((byte)0, Decode([], Centered((0x32, 65535))).RightTrigger);
        Assert.Equal((byte)255, Decode([], Centered((0x32, 0))).RightTrigger);

        // Both fully pressed leave Z at the center: indistinguishable from neither.
        RawGamepad both = Decode([], Centered((0x32, 0x8000)));
        Assert.Equal((byte)0, both.LeftTrigger);
        Assert.Equal((byte)0, both.RightTrigger);
    }

    [Fact]
    public void SeparateTriggers_UseZAndRz()
    {
        var layout = Ally with { Rz = Axis16 };
        var axes = Centered((0x32, 0), (0x35, 65535));

        RawGamepad pad = Decode([], axes, layout);

        Assert.False(layout.CombinedTriggers);
        Assert.Equal((byte)0, pad.LeftTrigger);
        Assert.Equal((byte)255, pad.RightTrigger);
    }

    [Fact]
    public void SignedLogicalMax_IsReadAsUnsigned()
    {
        Assert.Equal(new HidAxisRange(0, 65535), HidAxisRange.FromCaps(0, -1, 16));
        Assert.Equal(new HidAxisRange(1, 8), HidAxisRange.FromCaps(1, 8, 4));
    }
}

public class FallbackGamepadSourceTests
{
    private sealed class Fake(bool connected, RawGamepad state) : IGamepadSource
    {
        public RawGamepad State { get; set; } = state;

        public bool TryRead(out RawGamepad gamepad)
        {
            gamepad = State;
            return connected;
        }
    }

    private static readonly RawGamepad PressA = new(0x1000, 0, 0, 0, 0, 0, 0);
    private static readonly RawGamepad PressB = new(0x2000, 0, 0, 0, 0, 0, 0);

    [Fact]
    public void UsesXInput_WhileItHasData()
    {
        var source = new FallbackGamepadSource(new Fake(true, PressA), new Fake(true, PressB), () => true);

        Assert.True(source.TryRead(out RawGamepad pad));
        Assert.Equal(PressA, pad);
    }

    [Fact]
    public void UsesRawInput_WhenXInputIsIdle_LikeTheXboxFullScreen()
    {
        var source = new FallbackGamepadSource(new Fake(true, default), new Fake(true, PressB), () => true);

        for (int i = 0; i < 6; i++)
        {
            Assert.True(source.TryRead(out RawGamepad pad));
            Assert.Equal(PressB, pad);
        }

        Assert.True(source.UsingFallback);
    }

    [Fact]
    public void GoesBackToXInput_AsSoonAsItHasData()
    {
        var xinput = new Fake(true, default);
        var source = new FallbackGamepadSource(xinput, new Fake(true, PressB), () => true);
        for (int i = 0; i < 6; i++)
        {
            source.TryRead(out _);
        }

        xinput.State = PressA;
        source.TryRead(out RawGamepad pad);

        Assert.Equal(PressA, pad);
        Assert.False(source.UsingFallback);
    }

    [Fact]
    public void Disabled_OnlyXInput()
    {
        var source = new FallbackGamepadSource(new Fake(false, default), new Fake(true, PressB), () => false);

        Assert.False(source.TryRead(out _));
    }

    [Fact]
    public void WithoutXInput_RawInputStillWorks()
    {
        var source = new FallbackGamepadSource(new Fake(false, default), new Fake(true, PressB), () => true);

        Assert.True(source.TryRead(out RawGamepad pad));
        Assert.Equal(PressB, pad);
    }
}

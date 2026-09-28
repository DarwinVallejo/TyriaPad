using TyriaPad.Core.Config;
using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;

namespace TyriaPad.Core.Tests;

public class GestureDetectorTests
{
    private static readonly TimeSpan Ms = TimeSpan.FromMilliseconds(1);

    private readonly List<string> _gestures = [];
    private readonly GestureDetector _detector;

    public GestureDetectorTests()
    {
        var settings = new GestureSettings { HoldMs = 250, DoubleTapMs = 250 };
        _detector = new GestureDetector(settings, (button, gesture) => _gestures.Add($"{KeyNames.ButtonName(button)} {gesture}"));
    }

    [Fact]
    public void ShortPress_WithoutDouble_TapsOnRelease()
    {
        _detector.Press(GamepadButtons.A, 0 * Ms, hasHold: true, hasDouble: false);
        _detector.Update(100 * Ms);
        Assert.Empty(_gestures);

        _detector.Release(GamepadButtons.A, 100 * Ms);

        Assert.Equal(["A Tap"], _gestures);
    }

    [Fact]
    public void LongPress_FiresHoldStartAtThreshold_AndHoldEndOnRelease()
    {
        _detector.Press(GamepadButtons.A, 0 * Ms, hasHold: true, hasDouble: false);
        _detector.Update(249 * Ms);
        Assert.Empty(_gestures);

        _detector.Update(250 * Ms);
        _detector.Update(400 * Ms);
        Assert.Equal(["A HoldStart"], _gestures);

        _detector.Release(GamepadButtons.A, 500 * Ms);
        Assert.Equal(["A HoldStart", "A HoldEnd"], _gestures);
    }

    [Fact]
    public void LongPress_WithoutHoldBinding_IsStillATap()
    {
        _detector.Press(GamepadButtons.A, 0 * Ms, hasHold: false, hasDouble: false);
        _detector.Update(600 * Ms);
        _detector.Release(GamepadButtons.A, 600 * Ms);

        Assert.Equal(["A Tap"], _gestures);
    }

    [Fact]
    public void WithDouble_TapWaitsForWindow()
    {
        _detector.Press(GamepadButtons.A, 0 * Ms, hasHold: false, hasDouble: true);
        _detector.Release(GamepadButtons.A, 50 * Ms);
        _detector.Update(250 * Ms);
        Assert.Empty(_gestures);

        _detector.Update(301 * Ms);
        Assert.Equal(["A Tap"], _gestures);
    }

    [Fact]
    public void SecondPressWithinWindow_IsDoubleTap_AndSuppressesTap()
    {
        _detector.Press(GamepadButtons.A, 0 * Ms, hasHold: true, hasDouble: true);
        _detector.Release(GamepadButtons.A, 50 * Ms);
        _detector.Press(GamepadButtons.A, 200 * Ms, hasHold: true, hasDouble: true);
        Assert.Equal(["A DoubleTap"], _gestures);

        // The second press was already consumed: neither hold nor tap however long it lasts.
        _detector.Update(600 * Ms);
        _detector.Release(GamepadButtons.A, 600 * Ms);
        Assert.Equal(["A DoubleTap"], _gestures);
    }

    [Fact]
    public void SecondPressAfterWindow_FlushesTapThenStartsNewPress()
    {
        _detector.Press(GamepadButtons.A, 0 * Ms, hasHold: false, hasDouble: true);
        _detector.Release(GamepadButtons.A, 50 * Ms);
        _detector.Press(GamepadButtons.A, 400 * Ms, hasHold: false, hasDouble: true);
        Assert.Equal(["A Tap"], _gestures);

        _detector.Release(GamepadButtons.A, 450 * Ms);
        _detector.Update(800 * Ms);
        Assert.Equal(["A Tap", "A Tap"], _gestures);
    }

    [Fact]
    public void Buttons_AreIndependent()
    {
        _detector.Press(GamepadButtons.A, 0 * Ms, hasHold: true, hasDouble: false);
        _detector.Press(GamepadButtons.B, 100 * Ms, hasHold: true, hasDouble: false);
        _detector.Update(260 * Ms);
        Assert.Equal(["A HoldStart"], _gestures);

        _detector.Release(GamepadButtons.B, 300 * Ms);
        Assert.Equal(["A HoldStart", "B Tap"], _gestures);
    }

    [Fact]
    public void Reset_ForgetsPendingGestures()
    {
        _detector.Press(GamepadButtons.A, 0 * Ms, hasHold: true, hasDouble: true);
        _detector.Release(GamepadButtons.A, 50 * Ms);
        _detector.Reset();
        _detector.Update(1000 * Ms);
        _detector.Release(GamepadButtons.A, 1000 * Ms);

        Assert.Empty(_gestures);
    }
}

using TyriaPad.Core.Config;
using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;

namespace TyriaPad.Core.Tests;

/// <summary>The mapper with the default profile (reference layout). It starts in Action Camera.</summary>
public class ProfileMapperTests
{
    private static readonly TimeSpan Ms300 = TimeSpan.FromMilliseconds(300);

    private readonly MapperHarness _h = new();

    // --- Base layer ---

    [Fact]
    public void PressBinding_HoldsKeyWhilePressed()
    {
        _h.Step(GamepadButtons.RightBumper);
        _h.Step(GamepadButtons.RightBumper);

        Assert.Equal(["D1 down"], _h.Events);

        _h.Step(GamepadButtons.None);

        Assert.Equal(["D1 down", "D1 up"], _h.Events);
    }

    [Fact]
    public void DefaultStart_IsPointer_SoFirstToggleEntersActionCamera()
    {
        var h = new MapperHarness(initialMode: CameraMode.Pointer);
        Assert.Equal(CameraMode.Pointer, h.Mapper.Mode);
        Assert.Equal(GameContext.Pointer, h.Mapper.Context);

        h.Hold(GamepadButtons.Menu, Ms300);
        h.Step(GamepadButtons.None);
        h.Hold(GamepadButtons.None, TimeSpan.FromMilliseconds(100));
        h.Step(GamepadButtons.A);

        Assert.Equal(CameraMode.ActionCamera, h.Mapper.Mode);
        Assert.Equal(GameContext.Combat, h.Mapper.Context);
        Assert.Equal("Space down", h.Events[^1]);
    }

    [Fact]
    public void LeftStick_PressesMovementKeysWithHysteresis()
    {
        _h.Step(GamepadButtons.None, left: new Stick(0.7f, 0.7f));

        Assert.Equal(["W down", "D down"], _h.Events);

        _h.Step(GamepadButtons.None, left: new Stick(0.45f, 0.7f));

        Assert.Equal(2, _h.Events.Count);

        _h.Step(GamepadButtons.None, left: default);

        Assert.Equal(["W down", "D down", "W up", "D up"], _h.Events);
    }

    [Fact]
    public void RightStick_MovesCameraAtCameraSpeed()
    {
        _h.Hold(GamepadButtons.None, TimeSpan.FromSeconds(1), right: new Stick(1f, 0f));

        Assert.InRange(_h.Sink.TotalDx, 1300, 1500);
        Assert.Equal(0, _h.Sink.TotalDy);

        _h.Hold(GamepadButtons.None, TimeSpan.FromMilliseconds(100), right: new Stick(0f, 1f));

        Assert.True(_h.Sink.TotalDy < 0, "stick up must move the mouse up");
    }

    // --- Gestures ---

    [Fact]
    public void MenuTap_SendsEscape()
    {
        _h.Tap(GamepadButtons.Menu);

        Assert.Equal(["Escape down", "Escape up"], _h.Events);
        Assert.Equal(CameraMode.ActionCamera, _h.Mapper.Mode);
    }

    [Fact]
    public void MenuHold_TogglesActionCameraOnce()
    {
        _h.Hold(GamepadButtons.Menu, TimeSpan.FromMilliseconds(600));
        _h.Step(GamepadButtons.None);
        _h.Hold(GamepadButtons.None, InputEmitter.TapDuration);

        Assert.Equal(["Comma down", "Comma up"], _h.Events);
        Assert.Equal(CameraMode.Pointer, _h.Mapper.Mode);
    }

    [Fact]
    public void MenuLongHold_ResyncsModeWithoutSendingAnotherKey()
    {
        _h.Hold(GamepadButtons.Menu, TimeSpan.FromMilliseconds(2000));
        _h.Step(GamepadButtons.None);

        Assert.Equal(["Comma down", "Comma up"], _h.Events);
        Assert.Equal(CameraMode.ActionCamera, _h.Mapper.Mode);
    }

    [Fact]
    public void L3Tap_SendsHeal_L3Hold_OpensMountRadial()
    {
        _h.Tap(GamepadButtons.LeftStick);

        Assert.Equal(["D6 down", "D6 up"], _h.Events);
        _h.Events.Clear();

        _h.Hold(GamepadButtons.LeftStick, Ms300);

        Assert.NotNull(_h.Mapper.Radial);
        Assert.Equal("mounts", _h.Mapper.Radial!.Menu.Name);
        Assert.Empty(_h.Events);
    }

    [Fact]
    public void MountRadial_ChoosesWithRightStick_AndSendsChordOnRelease()
    {
        _h.Hold(GamepadButtons.LeftStick, Ms300);
        _h.Hold(GamepadButtons.LeftStick, TimeSpan.FromMilliseconds(50), right: new Stick(0f, 1f)); // up = Raptor

        Assert.Equal("Raptor", _h.Mapper.Radial!.SelectedItem!.Label);
        Assert.Equal(0, _h.Sink.TotalDx + _h.Sink.TotalDy); // the radial's stick doesn't move the camera

        _h.Step(GamepadButtons.None);
        _h.Hold(GamepadButtons.None, InputEmitter.TapDuration + MapperHarness.Tick);

        Assert.Null(_h.Mapper.Radial);
        Assert.Equal(["LeftShift down", "R down", "R up", "LeftShift up"], _h.Events);
    }

    [Fact]
    public void Radial_ReleasedWithoutPointing_SendsNothing()
    {
        _h.Hold(GamepadButtons.LeftStick, Ms300);
        _h.Step(GamepadButtons.None);
        _h.Hold(GamepadButtons.None, InputEmitter.TapDuration);

        Assert.Empty(_h.Events);
    }

    [Fact]
    public void R3_TapHoldAndDouble()
    {
        // Tap: since R3 has a double tap, it comes out when the window expires.
        _h.Step(GamepadButtons.RightStick);
        _h.Step(GamepadButtons.None);
        Assert.Empty(_h.Events);
        _h.Hold(GamepadButtons.None, Ms300);
        Assert.Equal(["Tab down", "Tab up"], _h.Events);
        _h.Events.Clear();

        // Double: second press within the window, no tap.
        _h.Step(GamepadButtons.RightStick);
        _h.Step(GamepadButtons.None);
        _h.Hold(GamepadButtons.None, TimeSpan.FromMilliseconds(100));
        _h.Step(GamepadButtons.RightStick);
        _h.Step(GamepadButtons.None);
        _h.Hold(GamepadButtons.None, Ms300);
        Assert.Equal(["T down", "T up"], _h.Events);
        _h.Events.Clear();

        // Hold: Ctrl+T held until release.
        _h.Hold(GamepadButtons.RightStick, Ms300);
        Assert.Equal(["LeftCtrl down", "T down"], _h.Events);
        _h.Step(GamepadButtons.None);
        Assert.Equal(["LeftCtrl down", "T down", "T up", "LeftCtrl up"], _h.Events);
    }

    // --- Layers ---

    [Fact]
    public void LB_IsPureModifier_AndRemapsRB()
    {
        _h.Step(GamepadButtons.LeftBumper);
        Assert.Empty(_h.Events);
        Assert.Equal("LB", _h.Mapper.ActiveLayer.Name);

        _h.Step(GamepadButtons.LeftBumper | GamepadButtons.RightBumper);
        Assert.Equal(["D3 down"], _h.Events);

        // LB is released before RB: the key that was pressed is the one released.
        _h.Step(GamepadButtons.RightBumper);
        Assert.Equal("base", _h.Mapper.ActiveLayer.Name);
        Assert.Equal(["D3 down"], _h.Events);

        _h.Step(GamepadButtons.None);
        Assert.Equal(["D3 down", "D3 up"], _h.Events);
    }

    [Fact]
    public void RT_IsSkill2Alone_ButModifierUnderLT()
    {
        _h.Step(GamepadButtons.RightTrigger);
        _h.Step(GamepadButtons.None);
        Assert.Equal(["D2 down", "D2 up"], _h.Events);
        _h.Events.Clear();

        _h.Step(GamepadButtons.LeftTrigger);
        _h.Step(GamepadButtons.LeftTrigger | GamepadButtons.RightTrigger);
        Assert.Empty(_h.Events);
        Assert.Equal("LT+RT", _h.Mapper.ActiveLayer.Name);

        _h.Step(GamepadButtons.LeftTrigger | GamepadButtons.RightTrigger | GamepadButtons.X);
        Assert.Equal(["F5 down"], _h.Events);

        _h.Step(GamepadButtons.LeftTrigger | GamepadButtons.X);
        Assert.Equal("LT", _h.Mapper.ActiveLayer.Name);
        _h.Step(GamepadButtons.LeftTrigger);
        Assert.Equal(["F5 down", "F5 up"], _h.Events);

        _h.Step(GamepadButtons.LeftTrigger | GamepadButtons.X);
        Assert.Equal("F1 down", _h.Events[^1]);
    }

    [Fact]
    public void LB_ThenLT_And_LT_ThenLB_BothSendSkill5()
    {
        _h.Step(GamepadButtons.LeftBumper);
        _h.Step(GamepadButtons.LeftBumper | GamepadButtons.LeftTrigger);
        _h.Step(GamepadButtons.None);
        Assert.Equal(["D5 down", "D5 up"], _h.Events);
        _h.Events.Clear();

        _h.Step(GamepadButtons.LeftTrigger);
        _h.Step(GamepadButtons.LeftTrigger | GamepadButtons.LeftBumper);
        _h.Step(GamepadButtons.None);
        Assert.Equal(["D5 down", "D5 up"], _h.Events);
    }

    [Fact]
    public void LB_Menu_ZoomsWithWheel_AndRepeats()
    {
        _h.Step(GamepadButtons.LeftBumper);
        _h.Hold(GamepadButtons.LeftBumper | GamepadButtons.Menu, TimeSpan.FromMilliseconds(450));

        Assert.Equal(["Wheel 120", "Wheel 120", "Wheel 120"], _h.Events);
    }

    // --- Contexts ---

    [Fact]
    public void PointerMode_AClicks_BCloses_XRightClicksOnTap()
    {
        _h.EnterPointerMode();

        _h.Step(GamepadButtons.A);
        _h.Step(GamepadButtons.B);
        _h.Step(GamepadButtons.X);
        _h.Step(GamepadButtons.None);
        _h.Hold(GamepadButtons.None, InputEmitter.TapDuration + MapperHarness.Tick);

        Assert.Equal(
            ["MouseLeft down", "MouseLeft up", "Escape down", "Escape up", "MouseRight down", "MouseRight up"],
            _h.Events);
    }

    [Fact]
    public void PointerMode_LBTap_RightClicks_LBHold_MovesWithLeftStick()
    {
        _h.EnterPointerMode();

        _h.Step(GamepadButtons.LeftBumper);
        _h.Step(GamepadButtons.None);
        _h.Hold(GamepadButtons.None, InputEmitter.TapDuration + MapperHarness.Tick);
        Assert.Equal(["MouseRight down", "MouseRight up"], _h.Events);
        _h.Events.Clear();

        _h.Hold(GamepadButtons.LeftBumper, Ms300, left: new Stick(0f, 1f));
        Assert.Equal(["W down"], _h.Events);
        Assert.Equal(0, _h.Sink.TotalDx + _h.Sink.TotalDy);

        _h.Step(GamepadButtons.LeftBumper | GamepadButtons.RightBumper, left: new Stick(0f, 1f));
        Assert.Equal(["W down", "LeftShift down", "MouseLeft down"], _h.Events);

        _h.Step(GamepadButtons.None);
        Assert.Contains("W up", _h.Events);
        Assert.DoesNotContain("MouseRight down", _h.Events); // LB used as a modifier: no tap
    }

    [Fact]
    public void PointerMode_BothSticksMoveCursor_AtPointerSpeed()
    {
        _h.EnterPointerMode();
        _h.Hold(GamepadButtons.None, TimeSpan.FromSeconds(1), left: new Stick(1f, 0f));

        Assert.InRange(_h.Sink.TotalDx, 800, 1000);
        Assert.Empty(_h.Events);
    }

    [Fact]
    public void MountContext_XSendsMountSkill2_RestInherited()
    {
        _h.Mapper.ObserveGame(new GameSignals(MountIndex: 4, MapOpen: false));
        _h.Step(GamepadButtons.X);
        Assert.Equal(GameContext.Mount, _h.Mapper.Context);
        Assert.Equal(["C down"], _h.Events);

        _h.Step(GamepadButtons.X | GamepadButtons.A);
        Assert.Equal(["C down", "Space down"], _h.Events);

        _h.Mapper.ObserveGame(default);
        _h.Step(GamepadButtons.None);
        Assert.Equal(GameContext.Combat, _h.Mapper.Context);
        Assert.Contains("C up", _h.Events);
    }

    [Fact]
    public void MapOpen_ForcesPointerContext_WithoutTouchingMode()
    {
        _h.Mapper.ObserveGame(new GameSignals(0, MapOpen: true));
        _h.Step(GamepadButtons.A);

        Assert.Equal(GameContext.Pointer, _h.Mapper.Context);
        Assert.Equal(CameraMode.ActionCamera, _h.Mapper.Mode);
        Assert.Equal(["MouseLeft down"], _h.Events);
    }

    [Fact]
    public void MapOpen_Ignored_WhenDisabled()
    {
        var h = new MapperHarness(settings: TyriaPadSettings.Default with { PointerWhenMapOpen = false });
        h.Mapper.ObserveGame(new GameSignals(0, MapOpen: true));
        h.Step(GamepadButtons.A);

        Assert.Equal(GameContext.Combat, h.Mapper.Context);
        Assert.Equal(["Space down"], h.Events);
    }

    // --- Cursor ---

    [Fact]
    public void CursorShownInActionCamera_SwitchesToPointer()
    {
        _h.ObserveCursor(visible: true, TimeSpan.FromMilliseconds(100));
        _h.Step(GamepadButtons.A);

        Assert.Equal(CameraMode.Pointer, _h.Mapper.Mode);
        Assert.Equal(["MouseLeft down"], _h.Events);

        _h.Step(GamepadButtons.None);
        _h.ObserveCursor(visible: false, TimeSpan.FromMilliseconds(100));
        _h.Step(GamepadButtons.A);

        Assert.Equal(CameraMode.ActionCamera, _h.Mapper.Mode);
        Assert.Equal("Space down", _h.Events[^1]);
    }

    [Fact]
    public void CursorFlicker_ShorterThanDebounce_IsIgnored()
    {
        _h.ObserveCursor(visible: true, TimeSpan.FromMilliseconds(40));
        _h.ObserveCursor(visible: false, TimeSpan.FromMilliseconds(40));

        Assert.Equal(CameraMode.ActionCamera, _h.Mapper.Mode);
    }

    [Fact]
    public void ManualToggle_IsNotUndoneWhileGameReacts()
    {
        _h.ObserveCursor(visible: false, TimeSpan.FromMilliseconds(200));
        _h.Hold(GamepadButtons.Menu, TimeSpan.FromMilliseconds(260));
        _h.Step(GamepadButtons.None);

        _h.ObserveCursor(visible: false, TimeSpan.FromMilliseconds(200));
        Assert.Equal(CameraMode.Pointer, _h.Mapper.Mode);

        _h.ObserveCursor(visible: true, TimeSpan.FromMilliseconds(600));
        Assert.Equal(CameraMode.Pointer, _h.Mapper.Mode);
    }

    // --- Safety ---

    [Fact]
    public void ModeChangeWhileHeld_ReleasesOriginalOutput()
    {
        _h.Step(GamepadButtons.A);
        _h.Hold(GamepadButtons.A | GamepadButtons.Menu, Ms300);
        _h.Step(GamepadButtons.Menu);

        Assert.Equal(CameraMode.Pointer, _h.Mapper.Mode);
        Assert.Contains("Space up", _h.Events);
        Assert.DoesNotContain("MouseLeft up", _h.Events);
    }

    [Fact]
    public void ButtonHeldBeforeActivation_IsIgnoredUntilReleased()
    {
        _h.Reactivate(GamepadButtons.Y);

        _h.Step(GamepadButtons.Y);
        _h.Step(GamepadButtons.None);
        _h.Hold(GamepadButtons.None, InputEmitter.TapDuration);

        Assert.Empty(_h.Events);

        _h.Tap(GamepadButtons.Y);

        Assert.Equal(["F down", "F up"], _h.Events);
    }

    [Fact]
    public void ModifierHeldBeforeActivation_DoesNotActivateLayer()
    {
        _h.Reactivate(GamepadButtons.LeftBumper);

        _h.Step(GamepadButtons.LeftBumper | GamepadButtons.RightBumper);

        Assert.Equal(["D1 down"], _h.Events);
    }

    [Fact]
    public void Deactivate_ReleasesEverything_IncludingRadialAndMovement()
    {
        _h.Step(GamepadButtons.RightTrigger, left: new Stick(0f, 1f));
        _h.Hold(GamepadButtons.RightTrigger | GamepadButtons.LeftStick, Ms300, left: new Stick(0f, 1f));
        Assert.NotNull(_h.Mapper.Radial);

        _h.Mapper.Deactivate();

        Assert.Contains("D2 up", _h.Events);
        Assert.Contains("W up", _h.Events);
        Assert.Null(_h.Mapper.Radial);
        Assert.Equal("base", _h.Mapper.ActiveLayer.Name);
    }
}

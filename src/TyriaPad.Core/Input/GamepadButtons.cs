namespace TyriaPad.Core.Input;

/// <summary>
/// Buttons in Xbox notation. The low bits match XINPUT_GAMEPAD; the triggers are added as digital
/// buttons so they can be mapped like the rest, and M1/M2 (the Ally X back buttons, read over HID)
/// above them.
/// </summary>
[Flags]
public enum GamepadButtons : uint
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Menu = 0x0010,
    View = 0x0020,
    LeftStick = 0x0040,
    RightStick = 0x0080,
    LeftBumper = 0x0100,
    RightBumper = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
    LeftTrigger = 0x1_0000,
    RightTrigger = 0x2_0000,
    M1 = 0x4_0000,
    M2 = 0x8_0000,
}
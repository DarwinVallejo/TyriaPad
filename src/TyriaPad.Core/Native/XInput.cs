using System.Runtime.InteropServices;

namespace TyriaPad.Core.Native;

internal static partial class XInput
{
    public const uint ErrorSuccess = 0;
    public const int MaxControllers = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct Gamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct State
    {
        public uint PacketNumber;
        public Gamepad Gamepad;
    }

    [LibraryImport("xinput1_4.dll")]
    public static partial uint XInputGetState(uint userIndex, out State state);
}
using System.Diagnostics;
using System.Runtime.InteropServices;

using TyriaPad.Core.Native;

namespace TyriaPad.Core.Diagnostics;

/// <summary>
/// Diagnostic (<c>--gameinput-probe</c>): reads the controller with GameInput (<c>GameInput.dll</c>, ships
/// with Windows) alongside XInput and logs every 10 s how many new readings each one gave. Used to see
/// whether GameInput keeps delivering the controller to TyriaPad in the background in the Xbox full
/// screen experience, where XInput delivers nothing. Also checks whether GameInput exposes the Ally X gyro.
/// Runs in two phases: the first <see cref="DefaultPhase"/> with the default focus policy (only the
/// foreground app gets the controller, as already seen on the Ally), then it asks for background input
/// with <c>SetFocusPolicy(GameInputEnableBackgroundInput)</c>. Read-only: it reprograms nothing.
/// </summary>
public sealed unsafe class GameInputProbe : IDisposable
{
    private const uint KindMotion = 0x00001000;
    private const uint KindGamepad = 0x00040000;

    // IGameInput::SetFocusPolicy is the last method of the v0 interface (after IUnknown and 18 methods).
    private const int SetFocusPolicySlot = 21;

    // GameInputFocusPolicy: 0 = default; EnableBackgroundInput = 0x40 in the GameInput versions that
    // turned background input off by default (the service can be newer than the DLL).
    private const uint EnableBackgroundInput = 0x00000040;
    private static readonly TimeSpan DefaultPhase = TimeSpan.FromSeconds(20);

    // Cap on button lines so the log doesn't grow unchecked.
    private const int MaxLoggedChanges = 300;
    private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    private readonly nint _gameInput;
    private readonly Thread? _thread;
    private readonly Timer? _summaryTimer;
    private readonly Timer? _phaseTimer;
    private volatile string _phase = "phase 1, default policy";
    private volatile bool _stopping;

    private int _gameInputReadings;
    private int _xinputPackets;
    private int _motionReadings;
    private int _failures;
    private GamepadState _last;
    private MotionState _lastMotion;
    private bool _hasMotion;

    public GameInputProbe()
    {
        string path = Path.Combine(Environment.SystemDirectory, "GameInput.dll");
        if (!File.Exists(path))
        {
            Log.Write("GameInput: GameInput.dll not found in System32");
            return;
        }

        Log.Write($"GameInput: {path}, version {FileVersionInfo.GetVersionInfo(path).FileVersion}");
        LogServiceVersion();
        if (!NativeLibrary.TryLoad(path, out nint library) || !NativeLibrary.TryGetExport(library, "GameInputCreate", out nint create))
        {
            Log.Write("GameInput: could not load GameInputCreate");
            return;
        }

        nint gameInput;
        int hr = ((delegate* unmanaged[Stdcall]<nint*, int>)create)(&gameInput);
        if (hr < 0 || gameInput == 0)
        {
            Log.Write($"GameInput: GameInputCreate failed (0x{hr:X8}); is the GameInputSvc service running?");
            return;
        }

        _gameInput = gameInput;
        Log.Write($"GameInput: created, reading gamepad and motion ({_phase}; background input is requested in {DefaultPhase.TotalSeconds:0} s)");
        _thread = new Thread(Run) { Name = "TyriaPad.GameInputProbe", IsBackground = true };
        _thread.Start();
        _summaryTimer = new Timer(_ => LogSummary(), null, SummaryInterval, SummaryInterval);
        _phaseTimer = new Timer(_ => EnableBackground(), null, DefaultPhase, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _stopping = true;
        _summaryTimer?.Dispose();
        _phaseTimer?.Dispose();
        _thread?.Join(TimeSpan.FromSeconds(1));
        if (_gameInput != 0)
        {
            Release(_gameInput);
        }
    }

    private void Run()
    {
        ulong lastTimestamp = 0;
        ulong lastMotionTimestamp = 0;
        uint lastPacket = 0;
        bool firstReading = true;
        bool firstMotion = true;
        int loggedChanges = 0;

        while (!_stopping)
        {
            if (XInput.XInputGetState(0, out XInput.State xstate) == XInput.ErrorSuccess && xstate.PacketNumber != lastPacket)
            {
                lastPacket = xstate.PacketNumber;
                Interlocked.Increment(ref _xinputPackets);
            }

            nint reading = GetCurrentReading(KindGamepad);
            if (reading == 0)
            {
                Interlocked.Increment(ref _failures);
            }
            else
            {
                ulong timestamp = GetTimestamp(reading);
                if (timestamp != lastTimestamp)
                {
                    lastTimestamp = timestamp;
                    Interlocked.Increment(ref _gameInputReadings);
                    GamepadState state;
                    if (((delegate* unmanaged[Stdcall]<nint, GamepadState*, byte>)Slot(reading, 22))(reading, &state) != 0)
                    {
                        if (firstReading)
                        {
                            firstReading = false;
                            LogFirstReading(reading);
                        }

                        if (state.Buttons != _last.Buttons && loggedChanges < MaxLoggedChanges)
                        {
                            loggedChanges++;
                            Log.Write($"GameInput: buttons [{DescribeButtons(state.Buttons)}]{(loggedChanges == MaxLoggedChanges ? " (limit reached; only counting from now on)" : "")}");
                        }

                        _last = state;
                    }
                }

                Release(reading);
            }

            nint motion = GetCurrentReading(KindMotion);
            if (motion != 0)
            {
                ulong timestamp = GetTimestamp(motion);
                MotionState state;
                if (timestamp != lastMotionTimestamp
                    && ((delegate* unmanaged[Stdcall]<nint, MotionState*, byte>)Slot(motion, 19))(motion, &state) != 0)
                {
                    lastMotionTimestamp = timestamp;
                    Interlocked.Increment(ref _motionReadings);
                    _lastMotion = state;
                    _hasMotion = true;
                    if (firstMotion)
                    {
                        firstMotion = false;
                        Log.Write("GameInput: motion readings arriving (gyro)");
                    }
                }

                Release(motion);
            }

            Thread.Sleep(PollInterval);
        }
    }

    /// <summary>Second phase: asks GameInput to deliver the controller even when TyriaPad isn't in the foreground.</summary>
    private void EnableBackground()
    {
        if (_stopping)
        {
            return;
        }

        // Logged before the call: if the method table weren't the expected one and the process crashed, we'd know why.
        Log.Write($"GameInput: calling SetFocusPolicy(0x{EnableBackgroundInput:X}) (method {SetFocusPolicySlot})");
        ((delegate* unmanaged[Stdcall]<nint, uint, void>)Slot(_gameInput, SetFocusPolicySlot))(_gameInput, EnableBackgroundInput);
        _phase = "phase 2, background input requested";
        Log.Write($"GameInput: {_phase}; keep moving the controller with GW2 in front");
    }

    /// <summary>
    /// The client (GameInput.dll) talks to a service that is updated separately (Microsoft Store or the
    /// GameInput installer); its version decides what the focus policy does.
    /// </summary>
    private static void LogServiceVersion()
    {
        foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            string root = Path.Combine(folder, "Microsoft GameInput");
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (string file in Directory.EnumerateFiles(root, "GameInput*", SearchOption.AllDirectories).Where(static f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    Log.Write($"GameInput: {file}, version {FileVersionInfo.GetVersionInfo(file).FileVersion}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Write($"GameInput: cannot list {root}: {ex.Message}");
            }

            return;
        }

        Log.Write("GameInput: no \"Microsoft GameInput\" folder in Program Files (only the Windows GameInput)");
    }

    private void LogFirstReading(nint reading)
    {
        // GetInputKind must include Gamepad; otherwise the method table isn't the expected one and the rest is worthless.
        uint kind = ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(reading, 3))(reading);
        string device = "?";
        nint devicePtr;
        ((delegate* unmanaged[Stdcall]<nint, nint*, void>)Slot(reading, 6))(reading, &devicePtr);
        if (devicePtr != 0)
        {
            // GameInputDeviceInfo starts with infoSize (uint32), vendorId and productId (uint16).
            byte* info = ((delegate* unmanaged[Stdcall]<nint, byte*>)Slot(devicePtr, 3))(devicePtr);
            if (info != null)
            {
                device = $"VID {*(ushort*)(info + 4):X4} PID {*(ushort*)(info + 6):X4}";
            }

            Release(devicePtr);
        }

        Log.Write($"GameInput: first gamepad reading, kind 0x{kind:X8}{((kind & KindGamepad) == 0 ? " (gamepad bit missing!)" : "")}, device {device}");
    }

    private void LogSummary()
    {
        GamepadState s = _last;
        string motion = _hasMotion
            ? $", gyro ({_lastMotion.AngularVelocityX:0.00}, {_lastMotion.AngularVelocityY:0.00}, {_lastMotion.AngularVelocityZ:0.00}) rad/s"
            : "";
        Log.Write($"GameInput [{_phase}]: {Interlocked.Exchange(ref _gameInputReadings, 0)} new readings in 10 s "
            + $"(XInput: {Interlocked.Exchange(ref _xinputPackets, 0)} packets; no reading: {Interlocked.Exchange(ref _failures, 0)}; "
            + $"motion: {Interlocked.Exchange(ref _motionReadings, 0)}); buttons [{DescribeButtons(s.Buttons)}], "
            + $"left stick ({s.LeftX:0.00}, {s.LeftY:0.00}), right ({s.RightX:0.00}, {s.RightY:0.00}), LT {s.LeftTrigger:0.00}, RT {s.RightTrigger:0.00}{motion}");
    }

    private nint GetCurrentReading(uint kind)
    {
        nint reading;
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, nint, nint*, int>)Slot(_gameInput, 4))(_gameInput, kind, 0, &reading);
        return hr < 0 ? 0 : reading;
    }

    private static ulong GetTimestamp(nint reading) => ((delegate* unmanaged[Stdcall]<nint, ulong>)Slot(reading, 5))(reading);

    private static void Release(nint obj) => ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 2))(obj);

    private static nint Slot(nint obj, int index) => (*(nint**)obj)[index];

    private static string DescribeButtons(uint buttons)
    {
        if (buttons == 0)
        {
            return "None";
        }

        string[] names = ["Menu", "View", "A", "B", "X", "Y", "Up", "Down", "Left", "Right", "LB", "RB", "LS", "RS"];
        List<string> pressed = [];
        for (int bit = 0; bit < names.Length; bit++)
        {
            if ((buttons & (1u << bit)) != 0)
            {
                pressed.Add(names[bit]);
            }
        }

        if (buttons >> names.Length != 0)
        {
            pressed.Add($"0x{buttons >> names.Length << names.Length:X}");
        }

        return string.Join(", ", pressed);
    }

    /// <summary>GameInputGamepadState (API v0).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GamepadState
    {
        public uint Buttons;
        public float LeftTrigger;
        public float RightTrigger;
        public float LeftX;
        public float LeftY;
        public float RightX;
        public float RightY;
    }

    /// <summary>GameInputMotionState (API v0), with padding at the end.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MotionState
    {
        public float AccelerationX;
        public float AccelerationY;
        public float AccelerationZ;
        public float AngularVelocityX;
        public float AngularVelocityY;
        public float AngularVelocityZ;
        public fixed float Rest[32];
    }
}

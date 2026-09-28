using System.Diagnostics;

using TyriaPad.Core.Diagnostics;
using TyriaPad.Core.Native;

namespace TyriaPad.Core.Input;

public interface IGamepadSource
{
    bool TryRead(out RawGamepad gamepad);
}

/// <summary>Reads the first connected XInput controller. Read-only: hides or virtualizes nothing.</summary>
public sealed class XInputSource : IGamepadSource
{
    // Querying empty slots is slow, so with no controller it only scans once a second.
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(1);

    private int _index = -1;
    private long _lastScan;

    public bool TryRead(out RawGamepad gamepad)
    {
        if (_index >= 0 && Read((uint)_index, out gamepad))
        {
            return true;
        }

        if (_index >= 0)
        {
            Log.Write($"XInput controller disconnected (slot {_index})");
            _index = -1;
        }

        gamepad = default;
        if (Stopwatch.GetElapsedTime(_lastScan) < RescanInterval)
        {
            return false;
        }

        _lastScan = Stopwatch.GetTimestamp();
        for (uint i = 0; i < XInput.MaxControllers; i++)
        {
            if (Read(i, out gamepad))
            {
                _index = (int)i;
                Log.Write($"XInput controller connected (slot {i})");
                return true;
            }
        }

        return false;
    }

    private static bool Read(uint index, out RawGamepad gamepad)
    {
        if (XInput.XInputGetState(index, out XInput.State state) != XInput.ErrorSuccess)
        {
            gamepad = default;
            return false;
        }

        XInput.Gamepad pad = state.Gamepad;
        gamepad = new RawGamepad(pad.Buttons, pad.LeftTrigger, pad.RightTrigger, pad.ThumbLX, pad.ThumbLY, pad.ThumbRX, pad.ThumbRY);
        return true;
    }
}
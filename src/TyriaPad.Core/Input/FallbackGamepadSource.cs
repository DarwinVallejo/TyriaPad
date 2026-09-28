using TyriaPad.Core.Diagnostics;

namespace TyriaPad.Core.Input;

/// <summary>
/// Uses the primary source (XInput) while it gives data and the fallback one (Raw Input) when the
/// primary is idle. In the Xbox full screen experience, Windows gives background apps an idle XInput
/// but Raw Input keeps arriving; on the desktop both match and XInput wins, since it has separate
/// triggers. It's decided on every read, so there's no need to know which mode Windows is in; the
/// log only reports when the switch holds.
/// </summary>
public sealed class FallbackGamepadSource(IGamepadSource primary, IGamepadSource fallback, Func<bool> enabled, Func<bool>? combinedTriggers = null) : IGamepadSource
{
    // Consecutive reads with XInput idle and Raw Input not, before assuming Xbox mode (avoids
    // reports caused by the one-cycle lag between the two sources when pressing on the desktop).
    private const int StreakToSwitch = 5;

    private int _streak;
    private bool _usingFallback;

    /// <summary>The last read with data came from the fallback source.</summary>
    public bool UsingFallback => _usingFallback;

    public bool TryRead(out RawGamepad gamepad)
    {
        bool hasPrimary = primary.TryRead(out RawGamepad main);
        if (!enabled())
        {
            gamepad = main;
            return hasPrimary;
        }

        if (hasPrimary && !IsIdle(main))
        {
            _streak = 0;
            if (_usingFallback)
            {
                _usingFallback = false;
                Log.Write("Controller: XInput is giving data again; reading through XInput");
            }

            gamepad = main;
            return true;
        }

        if (fallback.TryRead(out RawGamepad backup))
        {
            if (!IsIdle(backup) && !_usingFallback && ++_streak >= StreakToSwitch)
            {
                _usingFallback = true;
                Log.Write("Controller: XInput reads idle but Raw Input doesn't (Xbox full screen experience?); reading through Raw Input"
                    + (combinedTriggers?.Invoke() == true ? ". LT and RT share an axis: pressed together they cancel out and the LT+RT layer doesn't activate" : string.Empty));
            }

            gamepad = backup;
            return true;
        }

        gamepad = main;
        return hasPrimary;
    }

    /// <summary>Everything at rest (M1/M2 aside, which don't come from these sources).</summary>
    public static bool IsIdle(in RawGamepad g)
        => g.Buttons == 0 && g.LeftTrigger == 0 && g.RightTrigger == 0 && g.LeftX == 0 && g.LeftY == 0 && g.RightX == 0 && g.RightY == 0;
}

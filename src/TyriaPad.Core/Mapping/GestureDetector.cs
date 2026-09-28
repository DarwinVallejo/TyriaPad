using TyriaPad.Core.Config;
using TyriaPad.Core.Input;

namespace TyriaPad.Core.Mapping;

public enum Gesture
{
    /// <summary>Short press. Emitted on release or, if the button has a double tap, when the window expires.</summary>
    Tap,

    /// <summary>The button has been pressed for the hold threshold. <see cref="HoldEnd"/> follows on release.</summary>
    HoldStart,

    HoldEnd,

    /// <summary>Second press within the window. Emitted on press, not on release, and cancels the pending tap.</summary>
    DoubleTap,
}

/// <summary>
/// Turns press/release into tap, hold and double tap per button. It only delays the tap when the
/// button has a double tap; otherwise it emits it on release. Not thread-safe.
/// </summary>
public sealed class GestureDetector(GestureSettings settings, Action<GamepadButtons, Gesture> onGesture)
{
    private readonly Dictionary<GamepadButtons, ButtonState> _buttons = [];

    /// <param name="hasHold">The binding has a hold action (otherwise HoldStart is never emitted).</param>
    /// <param name="hasDouble">The binding has a double tap (otherwise the tap comes out on release).</param>
    public void Press(GamepadButtons button, TimeSpan now, bool hasHold, bool hasDouble)
    {
        ButtonState state = GetState(button);
        if (state.PendingTapAt is { } pendingAt)
        {
            state.PendingTapAt = null;
            if (now - pendingAt <= settings.DoubleTap)
            {
                state.PressedAt = now;
                state.Consumed = true;
                onGesture(button, Gesture.DoubleTap);
                return;
            }

            onGesture(button, Gesture.Tap);
        }

        state.PressedAt = now;
        state.HasHold = hasHold;
        state.HasDouble = hasDouble;
        state.HoldFired = false;
        state.Consumed = false;
    }

    public void Release(GamepadButtons button, TimeSpan now)
    {
        if (!_buttons.TryGetValue(button, out ButtonState? state) || state.PressedAt is null)
        {
            return;
        }

        state.PressedAt = null;
        if (state.Consumed)
        {
            return;
        }

        if (state.HoldFired)
        {
            onGesture(button, Gesture.HoldEnd);
        }
        else if (state.HasDouble)
        {
            state.PendingTapAt = now;
        }
        else
        {
            onGesture(button, Gesture.Tap);
        }
    }

    /// <summary>Emits the holds that reached the threshold and the taps whose double-tap window expired.</summary>
    public void Update(TimeSpan now)
    {
        foreach ((GamepadButtons button, ButtonState state) in _buttons)
        {
            if (state.PressedAt is { } pressedAt && state.HasHold && !state.HoldFired && !state.Consumed
                && now - pressedAt >= settings.Hold)
            {
                state.HoldFired = true;
                onGesture(button, Gesture.HoldStart);
            }

            if (state.PendingTapAt is { } pendingAt && now - pendingAt > settings.DoubleTap)
            {
                state.PendingTapAt = null;
                onGesture(button, Gesture.Tap);
            }
        }
    }

    /// <summary>Forgets everything without emitting anything (focus loss, profile change).</summary>
    public void Reset() => _buttons.Clear();

    private ButtonState GetState(GamepadButtons button)
    {
        if (!_buttons.TryGetValue(button, out ButtonState? state))
        {
            state = new ButtonState();
            _buttons[button] = state;
        }

        return state;
    }

    private sealed class ButtonState
    {
        public TimeSpan? PressedAt;
        public TimeSpan? PendingTapAt;
        public bool HasHold;
        public bool HasDouble;
        public bool HoldFired;
        public bool Consumed;
    }
}

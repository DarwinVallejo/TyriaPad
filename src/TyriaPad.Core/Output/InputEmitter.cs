namespace TyriaPad.Core.Output;

/// <summary>
/// Keeps track of what's pressed so everything can be released at once (focus loss, pause, chat)
/// and no keys get stuck. Reference counted: if two controller buttons send the same key, it's
/// only released when both are released. Chords (Shift+R) press the modifiers first and release
/// them last.
/// Not thread-safe: used only from the poller thread.
/// </summary>
public sealed class InputEmitter(IInputSink sink)
{
    /// <summary>
    /// How long a short press is held. Pressing and releasing at the same instant can fall
    /// within a single game frame and get lost.
    /// </summary>
    public static readonly TimeSpan TapDuration = TimeSpan.FromMilliseconds(40);

    /// <summary>Windows mouse wheel unit (WHEEL_DELTA).</summary>
    public const int WheelNotch = 120;

    private readonly Dictionary<OutputAction, int> _held = [];
    private readonly List<(Chord Chord, TimeSpan ReleaseAt)> _pendingTaps = [];

    public bool IsHeld(OutputAction action) => _held.ContainsKey(action);

    public void Press(OutputAction action)
    {
        if (action.IsNone)
        {
            return;
        }

        _held.TryGetValue(action, out int count);
        _held[action] = count + 1;
        if (count == 0)
        {
            Send(action, down: true);
        }
    }

    public void Release(OutputAction action)
    {
        if (!_held.TryGetValue(action, out int count))
        {
            return;
        }

        if (count > 1)
        {
            _held[action] = count - 1;
            return;
        }

        _held.Remove(action);
        Send(action, down: false);
    }

    public void Press(in Chord chord)
    {
        if (chord.IsNone)
        {
            return;
        }

        foreach (Key modifier in chord.ModifierKeys)
        {
            Press(modifier);
        }

        Press(chord.Action);
    }

    public void Release(in Chord chord)
    {
        if (chord.IsNone)
        {
            return;
        }

        Release(chord.Action);
        foreach (Key modifier in chord.ModifierKeys.Reverse())
        {
            Release(modifier);
        }
    }

    /// <summary>Presses now and releases after <see cref="TapDuration"/> (done by <see cref="Update"/>).</summary>
    public void Tap(OutputAction action, TimeSpan now) => Tap(new Chord(action), now);

    public void Tap(in Chord chord, TimeSpan now)
    {
        if (chord.IsNone)
        {
            return;
        }

        Press(chord);
        _pendingTaps.Add((chord, now + TapDuration));
    }

    /// <summary>Releases the short presses whose duration is up.</summary>
    public void Update(TimeSpan now)
    {
        for (int i = _pendingTaps.Count - 1; i >= 0; i--)
        {
            if (now >= _pendingTaps[i].ReleaseAt)
            {
                Release(_pendingTaps[i].Chord);
                _pendingTaps.RemoveAt(i);
            }
        }
    }

    public void MoveMouse(int dx, int dy)
    {
        if (dx != 0 || dy != 0)
        {
            sink.SendMouseMove(dx, dy);
        }
    }

    /// <param name="notches">Positive = wheel up (away from you), negative = down.</param>
    public void Wheel(int notches)
    {
        if (notches != 0)
        {
            sink.SendMouseWheel(notches * WheelNotch);
        }
    }

    public void ReleaseAll()
    {
        // Non-modifiers first, so Shift+R isn't released as R without Shift.
        foreach (OutputAction action in _held.Keys.OrderBy(static a => IsModifier(a) ? 1 : 0).ToArray())
        {
            Send(action, down: false);
        }

        _held.Clear();
        _pendingTaps.Clear();
    }

    private static bool IsModifier(OutputAction action)
        => action.Kind == OutputKind.Key && action.Key is Key.LeftShift or Key.LeftCtrl or Key.LeftAlt or Key.RightShift or Key.RightCtrl or Key.RightAlt;

    private void Send(OutputAction action, bool down)
    {
        if (action.Kind == OutputKind.Key)
        {
            sink.SendKey(action.Key, down);
        }
        else
        {
            sink.SendMouseButton(action.Button, down);
        }
    }
}

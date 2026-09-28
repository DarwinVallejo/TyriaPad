using System.Globalization;

namespace TyriaPad.Core.Input;

/// <summary>
/// Code an Ally vendor button sends through the ASUS HID collection (page FF31, usage 76).
/// Reports are <c>5A &lt;code&gt; …</c>. Some buttons send another code on release
/// (<see cref="Release"/>); otherwise the button counts as released when reports stop arriving.
/// </summary>
public readonly record struct AllyButtonCode(byte Press, byte? Release = null)
{
    /// <summary>"A5" (press only) or "A5/00" (press/release), in hexadecimal.</summary>
    public static bool TryParse(string? text, out AllyButtonCode code)
    {
        code = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length > 2 || !TryParseByte(parts[0], out byte press) || press == 0)
        {
            return false;
        }

        byte? release = null;
        if (parts.Length == 2)
        {
            if (!TryParseByte(parts[1], out byte value) || value == press)
            {
                return false;
            }

            release = value;
        }

        code = new AllyButtonCode(press, release);
        return true;
    }

    public override string ToString() => Release is byte release ? $"{Press:X2}/{release:X2}" : $"{Press:X2}";

    private static bool TryParseByte(string text, out byte value)
        => byte.TryParse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}

/// <summary>
/// Turns vendor reports into pressed buttons. Gets reports from the HID reader thread and is
/// queried from the poller thread, so everything goes under a lock.
/// </summary>
public sealed class AllyButtonDecoder
{
    public const byte ReportId = 0x5A;

    private readonly Lock _lock = new();
    private readonly KeyValuePair<GamepadButtons, AllyButtonCode>[] _codes;
    private readonly TimeSpan _releaseAfter;
    private readonly Dictionary<GamepadButtons, TimeSpan> _lastSeen = [];
    private GamepadButtons _down;
    private GamepadButtons _latched;

    /// <param name="releaseAfter">Buttons without a release code: released after this long without reports.</param>
    public AllyButtonDecoder(IReadOnlyDictionary<GamepadButtons, AllyButtonCode> codes, TimeSpan releaseAfter)
    {
        _codes = [.. codes];
        _releaseAfter = releaseAfter;
    }

    public IReadOnlyDictionary<GamepadButtons, AllyButtonCode> Codes => _codes.ToDictionary();

    /// <summary>The report's code, or null if it isn't a vendor report.</summary>
    public static byte? CodeOf(ReadOnlySpan<byte> report) => report.Length >= 2 && report[0] == ReportId ? report[1] : null;

    /// <summary>Applies a report. Returns false if its code belongs to no assigned button (nor 00).</summary>
    public bool OnReport(ReadOnlySpan<byte> report, TimeSpan now)
    {
        if (CodeOf(report) is not byte code)
        {
            return false;
        }

        lock (_lock)
        {
            if (code == 0)
            {
                // 00: nothing pressed.
                _down = GamepadButtons.None;
                return true;
            }

            bool known = false;
            foreach ((GamepadButtons button, AllyButtonCode map) in _codes)
            {
                if (code == map.Press)
                {
                    _down |= button;
                    _latched |= button;
                    _lastSeen[button] = now;
                    known = true;
                }
                else if (code == map.Release)
                {
                    _down &= ~button;
                    known = true;
                }
            }

            return known;
        }
    }

    /// <summary>
    /// Buttons pressed now. A press that started and ended between two queries is still delivered
    /// once, so a quick tap isn't lost while the poller runs at the slow rate.
    /// </summary>
    public GamepadButtons Poll(TimeSpan now)
    {
        lock (_lock)
        {
            foreach ((GamepadButtons button, AllyButtonCode map) in _codes)
            {
                if (map.Release is null && (_down & button) != 0 && now - _lastSeen[button] > _releaseAfter)
                {
                    _down &= ~button;
                }
            }

            GamepadButtons result = _down | _latched;
            _latched = GamepadButtons.None;
            return result;
        }
    }
}

/// <summary>
/// Recognizes the codes the controller sends on its own, at regular intervals, with nothing pressed.
/// On the Ally X, EC arrives every ~4 s; it's always dropped, and any other code that repeats at the
/// same cadence is treated the same way (other firmwares). Only used from the HID reader thread.
/// </summary>
public sealed class PeriodicCodeDetector
{
    /// <summary>Ally X heartbeat, seen in the log with GW2 open and the controller untouched.</summary>
    public const byte AllyXHeartbeat = 0xEC;

    private const int Samples = 4;
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(250);

    private readonly Dictionary<byte, Queue<TimeSpan>> _arrivals = [];

    public static bool IsKnownNoise(byte code) => code == AllyXHeartbeat;

    public void Observe(byte code, TimeSpan now)
    {
        if (!_arrivals.TryGetValue(code, out Queue<TimeSpan>? times))
        {
            times = new Queue<TimeSpan>();
            _arrivals[code] = times;
        }

        times.Enqueue(now);
        if (times.Count > Samples)
        {
            times.Dequeue();
        }
    }

    /// <summary>
    /// Codes that repeat on their own: at least three arrivals with intervals over 1 s that differ
    /// by no more than 250 ms, the last one less than two intervals ago. A person pressing a button
    /// isn't that regular.
    /// </summary>
    public IReadOnlyList<byte> Periodic(TimeSpan now)
    {
        var result = new List<byte>();
        foreach ((byte code, Queue<TimeSpan> queue) in _arrivals)
        {
            TimeSpan[] times = [.. queue];
            if (IsKnownNoise(code))
            {
                result.Add(code);
                continue;
            }

            if (times.Length < 3)
            {
                continue;
            }

            TimeSpan[] intervals = [.. times.Zip(times.Skip(1), static (a, b) => b - a)];
            TimeSpan min = intervals.Min();
            TimeSpan max = intervals.Max();
            if (min >= MinInterval && max - min <= Tolerance && now - times[^1] < 2 * max)
            {
                result.Add(code);
            }
        }

        return result;
    }
}

public enum LearnStatus
{
    /// <summary>Waiting for the <see cref="AllyButtonLearner.Current"/> button to be pressed.</summary>
    Waiting,

    /// <summary>Pressed: waiting for release to see whether it sends a release code.</summary>
    Capturing,
    Done,
    TimedOut,

    /// <summary>The button sends the same code as one already learned: over HID they can't be told apart.</summary>
    SameAsPrevious,
}

/// <summary>
/// Wizard to find out which code each button sends (the reference projects disagree and it changes
/// with the firmware). Each button is held for a moment and released: the first code is the press
/// code and, if a different one arrives on release, that's the release code. Writes nothing to the controller.
/// Codes that arrived before starting with nobody pressing anything (the Ally X sends EC every ~4 s)
/// are ignored: they belong to no button.
/// </summary>
public sealed class AllyButtonLearner
{
    private readonly Lock _lock = new();
    private readonly GamepadButtons[] _buttons;
    private readonly TimeSpan _waitTimeout;
    private readonly TimeSpan _captureWindow;
    private readonly Dictionary<GamepadButtons, AllyButtonCode> _learned = [];
    private readonly HashSet<byte> _background;
    private int _index;
    private TimeSpan _stepStart;
    private byte _press;
    private LearnStatus _status = LearnStatus.Waiting;
    private GamepadButtons _sameAs;

    /// <param name="background">Codes seen right before starting, with nothing pressed; 00 never counts as noise.</param>
    public AllyButtonLearner(IReadOnlyList<GamepadButtons> buttons, TimeSpan start, IEnumerable<byte>? background = null, TimeSpan? waitTimeout = null, TimeSpan? captureWindow = null)
    {
        _buttons = [.. buttons];
        _background = [.. (background ?? []).Where(static c => c != 0), PeriodicCodeDetector.AllyXHeartbeat];
        _stepStart = start;
        _waitTimeout = waitTimeout ?? TimeSpan.FromSeconds(20);
        _captureWindow = captureWindow ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>Raised (on the thread calling Feed/Tick) when each button starts and when it's done.</summary>
    public event Action<AllyButtonLearner>? Progress;

    public LearnStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    /// <summary>Button being learned; None when done.</summary>
    public GamepadButtons Current
    {
        get
        {
            lock (_lock)
            {
                return _index < _buttons.Length ? _buttons[_index] : GamepadButtons.None;
            }
        }
    }

    /// <summary>With <see cref="LearnStatus.SameAsPrevious"/>: the already learned button whose code <see cref="Current"/> repeats.</summary>
    public GamepadButtons SameAs
    {
        get
        {
            lock (_lock)
            {
                return _sameAs;
            }
        }
    }

    public IReadOnlyDictionary<GamepadButtons, AllyButtonCode> Learned
    {
        get
        {
            lock (_lock)
            {
                return _learned.ToDictionary();
            }
        }
    }

    public void Feed(ReadOnlySpan<byte> report, TimeSpan now)
    {
        if (AllyButtonDecoder.CodeOf(report) is not byte code || _background.Contains(code))
        {
            return;
        }

        bool changed;
        lock (_lock)
        {
            changed = FeedLocked(code, now) | TickLocked(now);
        }

        if (changed)
        {
            Progress?.Invoke(this);
        }
    }

    /// <summary>Advances by time (end of the capture window or of the wait timeout).</summary>
    public void Tick(TimeSpan now)
    {
        bool changed;
        lock (_lock)
        {
            changed = TickLocked(now);
        }

        if (changed)
        {
            Progress?.Invoke(this);
        }
    }

    private bool FeedLocked(byte code, TimeSpan now)
    {
        switch (_status)
        {
            case LearnStatus.Waiting when _learned.FirstOrDefault(l => l.Value.Press == code) is { Key: not GamepadButtons.None } same:
                // On the Ally X, M1 and M2 both send 5A A5: all we know is that "a back button" was pressed.
                _sameAs = same.Key;
                _status = LearnStatus.SameAsPrevious;
                return true;
            case LearnStatus.Waiting when code != 0 && !IsUsed(code):
                _press = code;
                _stepStart = now;
                _status = LearnStatus.Capturing;
                return false;
            case LearnStatus.Capturing when code != _press:
                // The next code other than the press code is the release code (can be 00).
                Finish(new AllyButtonCode(_press, code), now);
                return true;
            default:
                return false;
        }
    }

    private bool TickLocked(TimeSpan now)
    {
        switch (_status)
        {
            case LearnStatus.Capturing when now - _stepStart >= _captureWindow:
                Finish(new AllyButtonCode(_press), now);
                return true;
            case LearnStatus.Waiting when now - _stepStart >= _waitTimeout:
                _status = LearnStatus.TimedOut;
                return true;
            default:
                return false;
        }
    }

    private void Finish(AllyButtonCode code, TimeSpan now)
    {
        _learned[_buttons[_index]] = code;
        _index++;
        _stepStart = now;
        _status = _index < _buttons.Length ? LearnStatus.Waiting : LearnStatus.Done;
    }

    // Codes already learned (release codes too, which can arrive late) don't count for the next button.
    private bool IsUsed(byte code) => _learned.Values.Any(c => c.Press == code || c.Release == code);
}

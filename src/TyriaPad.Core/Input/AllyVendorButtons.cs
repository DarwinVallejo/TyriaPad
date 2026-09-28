using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using Microsoft.Win32.SafeHandles;

using TyriaPad.Core.Config;
using TyriaPad.Core.Diagnostics;
using TyriaPad.Core.Native;

namespace TyriaPad.Core.Input;

/// <summary>
/// Reads the Ally X M1/M2 through the ASUS HID collection (VID 0B05, page FF31, usage 76) and adds
/// them to what XInput delivers. Read-only and shared: it sends no feature reports and doesn't reprogram
/// the controller, so Armoury Crate SE and the rest of the system still see the buttons the same way.
/// Each button's codes are saved in <c>allybuttons.json</c>, next to the executable.
/// </summary>
public sealed class AllyVendorButtons : IDisposable
{
    public const string FileName = "allybuttons.json";
    public static readonly GamepadButtons[] Learnable = [GamepadButtons.M1, GamepadButtons.M2];

    private const ushort AsusVendorId = 0x0B05;
    private const ushort VendorUsagePage = 0xFF31;
    private const ushort VendorUsage = 0x76;
    private const int MaxUnknownLogged = 32;
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LearnTick = TimeSpan.FromMilliseconds(100);

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly string _path;
    private readonly ManualResetEventSlim _stop = new();
    private readonly HashSet<byte> _unknownLogged = [];
    private readonly PeriodicCodeDetector _periodic = new();
    private byte? _lastLearnCode;
    private AllyButtonsSettings _settings;
    private IReadOnlyDictionary<GamepadButtons, AllyButtonCode> _codes;
    private volatile AllyButtonDecoder _decoder;
    private volatile AllyButtonLearner? _learner;
    private Timer? _learnTimer;
    private Thread? _thread;
    private SafeFileHandle? _handle;
    private volatile bool _connected;

    public AllyVendorButtons(string directory, AllyButtonsSettings settings)
    {
        _path = Path.Combine(directory, FileName);
        _settings = settings;
        _codes = Load(_path);
        _decoder = new AllyButtonDecoder(_codes, settings.ReleaseAfter);
        Log.Write(_codes.Count == 0
            ? "M1/M2: no learned codes (tray → \"Learn Ally M1/M2\")"
            : $"M1/M2: codes {Describe(_codes)}{(File.Exists(_path) ? string.Empty : $" (the Ally X ones; {FileName} doesn't exist)")}");
        if (settings.Enabled)
        {
            StartReader();
        }
    }

    /// <summary>The ASUS collection is open (that is, we're on an Ally).</summary>
    public bool IsConnected => _connected;

    public IReadOnlyDictionary<GamepadButtons, AllyButtonCode> Codes => _codes;

    /// <summary>XInput plus M1/M2.</summary>
    public IGamepadSource Wrap(IGamepadSource inner) => new Source(inner, this);

    public void Apply(AllyButtonsSettings settings)
    {
        AllyButtonsSettings old = _settings;
        _settings = settings;
        if (settings.ReleaseAfter != old.ReleaseAfter)
        {
            _decoder = new AllyButtonDecoder(_codes, settings.ReleaseAfter);
        }

        if (settings.Enabled && _thread is null)
        {
            StartReader();
        }
        else if (!settings.Enabled && _thread is not null)
        {
            StopReader();
            Log.Write("M1/M2: disabled in config.json");
        }
    }

    /// <summary>
    /// Starts learning M1 and M2 (in that order). While it lasts, the buttons aren't passed to the mapping.
    /// When it finishes successfully they're saved and used right away. <paramref name="progress"/> comes from another thread.
    /// </summary>
    public AllyButtonLearner? StartLearning(Action<AllyButtonLearner> progress)
    {
        if (!_connected)
        {
            return null;
        }

        CancelLearning();
        IReadOnlyList<byte> background;
        lock (_periodic)
        {
            background = _periodic.Periodic(Now());
        }

        _lastLearnCode = null;
        var learner = new AllyButtonLearner(Learnable, Now(), background);
        learner.Progress += OnLearnProgress;
        learner.Progress += progress;
        _learner = learner;
        _learnTimer = new Timer(_ => learner.Tick(Now()), null, LearnTick, LearnTick);
        Log.Write(background.Count == 0
            ? "M1/M2: learning codes"
            : $"M1/M2: learning codes; ignoring the ones that arrive on their own: {string.Join(", ", background.Select(static c => c.ToString("X2")))}");
        return learner;
    }

    public void CancelLearning()
    {
        _learnTimer?.Dispose();
        _learnTimer = null;
        _learner = null;
    }

    public void Dispose()
    {
        CancelLearning();
        StopReader();
        _stop.Dispose();
    }

    private TimeSpan Now() => Stopwatch.GetElapsedTime(_start);

    private void OnLearnProgress(AllyButtonLearner learner)
    {
        if (learner != _learner)
        {
            return;
        }

        switch (learner.Status)
        {
            case LearnStatus.Done:
                _learnTimer?.Dispose();
                _learnTimer = null;
                _learner = null;
                _codes = learner.Learned;
                _decoder = new AllyButtonDecoder(_codes, _settings.ReleaseAfter);
                Save(_path, _codes);
                Log.Write($"M1/M2: learned {Describe(_codes)}");
                break;
            case LearnStatus.TimedOut:
                _learnTimer?.Dispose();
                _learnTimer = null;
                _learner = null;
                Log.Write($"M1/M2: learning canceled, nothing arrived from {learner.Current}");
                break;
            case LearnStatus.SameAsPrevious:
                // What was learned so far is saved: the first button does work (and the other one does the same).
                _learnTimer?.Dispose();
                _learnTimer = null;
                _learner = null;
                _codes = learner.Learned;
                _decoder = new AllyButtonDecoder(_codes, _settings.ReleaseAfter);
                Save(_path, _codes);
                Log.Write($"M1/M2: {learner.Current} sends the same code as {learner.SameAs} ({_codes[learner.SameAs]}); "
                    + $"over HID they can't be told apart. Saving {Describe(_codes)}");
                break;
        }
    }

    private void StartReader()
    {
        _stop.Reset();
        _thread = new Thread(Run) { Name = "TyriaPad.AllyButtons", IsBackground = true };
        _thread.Start();
    }

    private void StopReader()
    {
        _stop.Set();
        if (Volatile.Read(ref _handle) is { } handle)
        {
            Hid.CancelIoEx(handle, 0);
        }

        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    private void Run()
    {
        bool reportedMissing = false;
        while (!_stop.IsSet)
        {
            if (Open() is ({ } handle, int length))
            {
                reportedMissing = false;
                Volatile.Write(ref _handle, handle);
                _connected = true;
                Log.Write("M1/M2: reading the Ally buttons over HID");
                ReadLoop(handle, length);
                _connected = false;
                Volatile.Write(ref _handle, null);
                handle.Dispose();
            }
            else if (!reportedMissing)
            {
                // Normal outside the Ally: keeps looking in case the device shows up later.
                Log.Write("M1/M2: ASUS HID collection not found (not an Ally?)");
                reportedMissing = true;
            }

            _stop.Wait(RetryInterval);
        }
    }

    private static (SafeFileHandle Handle, int Length)? Open()
    {
        foreach (string path in HidDevices.EnumeratePaths())
        {
            if (HidDevices.Describe(path) is not { VendorId: AsusVendorId, UsagePage: VendorUsagePage, Usage: VendorUsage } info
                || info.InputReportLength == 0)
            {
                continue;
            }

            SafeFileHandle handle = HidDevices.OpenForRead(path);
            if (!handle.IsInvalid)
            {
                return (handle, info.InputReportLength);
            }

            Log.Write($"M1/M2: cannot open the ASUS collection (error {Marshal.GetLastPInvokeError()})");
            handle.Dispose();
        }

        return null;
    }

    private unsafe void ReadLoop(SafeFileHandle handle, int length)
    {
        byte[] buffer = new byte[length];
        while (!_stop.IsSet)
        {
            uint read;
            bool ok;
            fixed (byte* pointer = buffer)
            {
                ok = Hid.ReadFile(handle, pointer, (uint)length, out read, 0);
            }

            if (!ok)
            {
                if (!_stop.IsSet)
                {
                    Log.Write($"M1/M2: HID read failed (error {Marshal.GetLastPInvokeError()}); retrying");
                }

                return;
            }

            OnReport(buffer.AsSpan(0, (int)read));
        }
    }

    private void OnReport(ReadOnlySpan<byte> report)
    {
        TimeSpan now = Now();
        if (AllyButtonDecoder.CodeOf(report) is not byte code)
        {
            return;
        }

        lock (_periodic)
        {
            _periodic.Observe(code, now);
        }

        if (_learner is { } learner)
        {
            // Everything that arrives while learning goes to the log, in case the result doesn't add up.
            if (code != _lastLearnCode)
            {
                _lastLearnCode = code;
                Log.Write($"M1/M2 (learning {learner.Current}): {Convert.ToHexString(report)}");
            }

            learner.Feed(report, now);
            return;
        }

        if (!_decoder.OnReport(report, now) && !PeriodicCodeDetector.IsKnownNoise(code)
            && _unknownLogged.Count < MaxUnknownLogged && _unknownLogged.Add(code))
        {
            // Other vendor buttons (Armoury Crate button, Command Center…) or M1/M2 not learned yet.
            Log.Write($"M1/M2: unassigned code {code:X2} ({Convert.ToHexString(report)})");
        }
    }

    private static string Describe(IReadOnlyDictionary<GamepadButtons, AllyButtonCode> codes)
        => string.Join(", ", codes.Select(static c => $"{c.Key} = {c.Value}"));

    /// <summary>Reads <c>{"M1": "A5", "M2": "A8/00"}</c>. Public so it can be tested.</summary>
    public static Dictionary<GamepadButtons, AllyButtonCode> Parse(string json)
    {
        var result = new Dictionary<GamepadButtons, AllyButtonCode>();
        foreach ((string name, string value) in JsonSerializer.Deserialize<Dictionary<string, string>>(json, s_json) ?? [])
        {
            // EC is the Ally X heartbeat: an earlier version could learn it as M2 by mistake.
            if (Enum.TryParse(name, ignoreCase: true, out GamepadButtons button) && Learnable.Contains(button)
                && AllyButtonCode.TryParse(value, out AllyButtonCode code) && !PeriodicCodeDetector.IsKnownNoise(code.Press))
            {
                result[button] = code;
            }
            else
            {
                Log.Write($"{FileName}: ignoring \"{name}\": \"{value}\"");
            }
        }

        return result;
    }

    /// <summary>
    /// Codes verified on the ROG Ally X: M1 sends A5 and 00 on release, and M2 sends the same (they
    /// can't be told apart through this collection). Used if there's no <see cref="FileName"/>, so M1
    /// works without learning it, also after moving the program to another folder. Learning the buttons replaces them.
    /// </summary>
    public static IReadOnlyDictionary<GamepadButtons, AllyButtonCode> DefaultCodes { get; } =
        new Dictionary<GamepadButtons, AllyButtonCode> { [GamepadButtons.M1] = new(0xA5, 0x00) };

    public static string Serialize(IReadOnlyDictionary<GamepadButtons, AllyButtonCode> codes)
        => JsonSerializer.Serialize(codes.ToDictionary(static c => c.Key.ToString(), static c => c.Value.ToString()), s_json);

    private static Dictionary<GamepadButtons, AllyButtonCode> Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : new Dictionary<GamepadButtons, AllyButtonCode>(DefaultCodes);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not read {path}: {ex.Message}");
            return [];
        }
    }

    private static void Save(string path, IReadOnlyDictionary<GamepadButtons, AllyButtonCode> codes)
    {
        try
        {
            File.WriteAllText(path, Serialize(codes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not save {path}: {ex.Message}");
        }
    }

    private sealed class Source(IGamepadSource inner, AllyVendorButtons owner) : IGamepadSource
    {
        public bool TryRead(out RawGamepad gamepad)
        {
            if (!inner.TryRead(out gamepad))
            {
                return false;
            }

            // While learning, M1/M2 aren't passed on: the user is pressing them on purpose.
            if (owner._connected && owner._learner is null)
            {
                gamepad = gamepad with { Extra = owner._decoder.Poll(owner.Now()) };
            }

            return true;
        }
    }
}

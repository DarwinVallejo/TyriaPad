using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

using TyriaPad.App.Overlay;
using TyriaPad.Core;
using TyriaPad.Core.Config;
using TyriaPad.Core.Diagnostics;
using TyriaPad.Core.Game;
using TyriaPad.Core.Input;
using TyriaPad.Core.Keybinds;
using TyriaPad.Core.Output;
using TyriaPad.Core.Overlay;

namespace TyriaPad.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private FocusWatcher? _focus;
    private GameProcessMonitor? _gameProcess;
    private MumbleLinkReader? _mumble;
    private ConfigStore? _config;
    private InputBindsStore? _keybinds;
    private TyriaPadEngine? _engine;
    private OverlayController? _overlay;
    private IDisposable? _preview;
    private TrayIcon? _tray;
    private Timer? _heartbeat;
    private HidProbe? _hidProbe;
    private GameInputProbe? _gameInputProbe;
    private RawInputProbe? _rawInputProbe;
    private RawInputGamepad? _rawGamepad;
    private AllyVendorButtons? _allyButtons;

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Software rendering: with the GPU, WPF started with ~90 MB more (Direct3D) and the overlay leaked
        // native memory on every repaint (~15 MB/min with the preview, also on the Ally in Xbox
        // mode). In software it stays at ~45 MB private and ~0.2% CPU: the overlay repaints rarely.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Diagnostics without GW2: TyriaPad.exe --overlay-snapshot <folder> saves PNGs of the overlay and exits.
        int snapshot = Array.FindIndex(e.Args, static a => a.Equals("--overlay-snapshot", StringComparison.OrdinalIgnoreCase));
        if (snapshot >= 0)
        {
            string directory = snapshot + 1 < e.Args.Length ? e.Args[snapshot + 1] : Path.Combine(AppContext.BaseDirectory, "overlay-snapshots");
            OverlayPreview.Snapshot(Defaults.Load(), directory);
            Shutdown();
            return;
        }

        // Diagnostics: TyriaPad.exe --settings-snapshot <folder> saves each settings tab as a PNG and exits.
        int settingsSnapshot = Array.FindIndex(e.Args, static a => a.Equals("--settings-snapshot", StringComparison.OrdinalIgnoreCase));
        if (settingsSnapshot >= 0)
        {
            string directory = settingsSnapshot + 1 < e.Args.Length ? e.Args[settingsSnapshot + 1] : Path.Combine(AppContext.BaseDirectory, "settings-snapshots");
            using var store = new ConfigStore(Path.Combine(Path.GetTempPath(), "TyriaPad-settings-snapshot"));
            store.Start();
            Settings.SettingsWindow.Snapshot(store, directory);
            Shutdown();
            return;
        }

        // Two instances would send every key twice.
        _singleInstance = new Mutex(true, @"Local\TyriaPad", out bool createdNew);
        if (!createdNew)
        {
            // No message box: in the Xbox full screen experience the box stays hidden and steals focus.
            Shutdown();
            return;
        }

        // Next to the executable, like config.json: the folder is the whole program.
        Log.Initialize(Path.Combine(AppContext.BaseDirectory, "tyriapad.log"));
        Log.Write($"TyriaPad {typeof(App).Assembly.GetName().Version} started (pid {Environment.ProcessId}){(e.Args.Length > 0 ? $", arguments: {string.Join(' ', e.Args)}" : "")}, built {BuildTime()}");

        var config = new ConfigStore(AppContext.BaseDirectory);
        _config = config;
        LoadedConfig loaded = config.Start();

        var focus = new FocusWatcher();
        var mumble = new MumbleLinkReader();
        _focus = focus;
        _mumble = mumble;
        var gameFocus = new GameFocusMonitor(focus, mumble);

        // Without GW2 open, the controller is read at the idle rate. When GW2 closes TyriaPad exits if so
        // configured, unless Windows startup launched it (then it waits for the next session).
        bool autostart = e.Args.Contains(StartupShortcut.Argument, StringComparer.OrdinalIgnoreCase);
        var gameProcess = new GameProcessMonitor(focus);
        _gameProcess = gameProcess;
        gameProcess.Exited += () =>
        {
            if (!autostart && config.Current.Settings.ExitWithGame)
            {
                Log.Write("Closing TyriaPad because GW2 closed (exitWithGame)");
                Dispatcher.InvokeAsync(Shutdown);
            }
            else
            {
                Dispatcher.InvokeAsync(() => MemoryTrim.Now("GW2 closed, idle"));
            }
        };
        if (!gameProcess.IsRunning)
        {
            Log.Write("GW2 is not open: idle until it starts");
        }

        var output = new LoggingInputSink(new SendInputSink());

        // Ally X M1/M2 over HID, added to what arrives through XInput. Does nothing outside the Ally.
        var allyButtons = new AllyVendorButtons(AppContext.BaseDirectory, loaded.Settings.AllyButtons);
        _allyButtons = allyButtons;
        config.Changed += c => allyButtons.Apply(c.Settings.AllyButtons);
        // XInput while it gives data; if it only reports idle (Xbox full screen experience), Raw Input.
        // --rawinput-probe registers the same collections and would take its reports away, so it isn't used with it.
        IGamepadSource gamepad = new XInputSource();
        if (e.Args.Contains("--rawinput-probe", StringComparer.OrdinalIgnoreCase))
        {
            Log.Write("Raw Input controller disabled while --rawinput-probe runs");
        }
        else
        {
            var rawGamepad = new RawInputGamepad();
            _rawGamepad = rawGamepad;
            gamepad = new FallbackGamepadSource(gamepad, rawGamepad, () => config.Current.Settings.Input.RawInputFallback, () => rawGamepad.CombinedTriggers);
        }

        _engine = new TyriaPadEngine(
            allyButtons.Wrap(gamepad),
            output,
            gameFocus.IsGameFocused,
            mumble.IsTextboxFocused,
            CursorProbe.IsCursorVisible,
            mumble.GetSignals,
            loaded,
            () => gameProcess.IsRunning);
        config.Changed += _engine.Apply;

        // GW2 keybinds: they place the glyphs and warn about profile keys that do nothing.
        var keybinds = new InputBindsStore(loaded.Settings.InputBinds);
        _keybinds = keybinds;
        keybinds.Start();
        CheckKeybinds();
        config.Changed += c =>
        {
            // If the file changed, the tray checks when it gets keybinds.Changed.
            if (!keybinds.SetSetting(c.Settings.InputBinds))
            {
                CheckKeybinds();
            }
        };

        _overlay = new OverlayController(_engine, focus, mumble, config, new CalibrationStore(AppContext.BaseDirectory), keybinds);
        _tray = new TrayIcon(_engine, config, _overlay, keybinds, allyButtons, CheckKeybinds, Shutdown);
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            _tray.OpenSettings();
        }

        _engine.Start();

        // --xbox-probe: sample overlay to see whether it draws over GW2 in the Xbox full screen experience.
        // The controller is already read there through Raw Input, and the log records every button that arrives.
        bool xboxProbe = e.Args.Contains("--xbox-probe", StringComparer.OrdinalIgnoreCase);
        if (xboxProbe)
        {
            Log.Write("Xbox mode test: sample overlay. With GW2 in front, press every button, LT alone, RT alone and both together");
        }

        // Diagnostics without GW2 (TyriaPad.exe --overlay-preview): sample overlay over the desktop.
        if (xboxProbe || e.Args.Contains("--overlay-preview", StringComparer.OrdinalIgnoreCase))
        {
            _preview = OverlayPreview.Show(loaded);
        }

        // Optional diagnostics (TyriaPad.exe --hid-probe): logs the Ally X HID reports.
        // Used to decode M1/M2 in phase 5.
        if (e.Args.Contains("--hid-probe", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _hidProbe = new HidProbe();
            }
            catch (Exception ex)
            {
                Log.Write($"HID: the probe failed: {ex}");
            }
        }

        // Optional diagnostics (TyriaPad.exe --gameinput-probe): also reads the controller with GameInput and
        // compares with XInput, to see whether GameInput arrives in the background in Xbox mode.
        if (e.Args.Contains("--gameinput-probe", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _gameInputProbe = new GameInputProbe();
            }
            catch (Exception ex)
            {
                Log.Write($"GameInput: the probe failed: {ex}");
            }
        }

        // Optional diagnostics (TyriaPad.exe --rawinput-probe): the controller's HID reports through Raw Input with
        // INPUTSINK, another way to receive the controller in the background in Xbox mode.
        if (e.Args.Contains("--rawinput-probe", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _rawInputProbe = new RawInputProbe();
            }
            catch (Exception ex)
            {
                Log.Write($"RawInput: the probe failed: {ex}");
            }
        }

        // Heartbeat: if the log stops, it shows until when TyriaPad stayed alive and in what state.
        TyriaPadEngine engine = _engine;
        var usage = new ResourceUsage();
        _heartbeat = new Timer(_ =>
        {
            Log.Write(DescribeHeartbeat(engine, focus, gameProcess, mumble, output, usage));
            MemoryTrim.CollectIfGrown();
        }, null, HeartbeatInterval, HeartbeatInterval);
        MemoryTrim.After(TimeSpan.FromSeconds(5), "startup finished");
    }

    /// <summary>Checks the profile against the GW2 keybinds and writes the warnings to the log.</summary>
    private IReadOnlyList<string> CheckKeybinds()
    {
        if (_config is null || _keybinds is null)
        {
            return [];
        }

        Gw2Keybinds binds = _keybinds.Current;
        IReadOnlyList<string> issues = KeybindCheck.Analyze(_config.Current.Profile, _config.Current.Settings, binds);
        string source = binds.Source ?? "GW2 default keys";
        Log.Write(issues.Count == 0
            ? $"GW2 keybinds ({source}): no warnings"
            : $"GW2 keybinds ({source}): {issues.Count} warnings{Environment.NewLine}  - {string.Join(Environment.NewLine + "  - ", issues)}");
        return issues;
    }

    private static string BuildTime() =>
        typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(static a => a.Key == "BuildTime")?.Value ?? "?";

    private static string DescribeHeartbeat(TyriaPadEngine engine, FocusWatcher focus, GameProcessMonitor gameProcess, MumbleLinkReader mumble, LoggingInputSink output, ResourceUsage usage)
    {
        (long dx, long dy) = output.TakeMouseTotals();
        string running = gameProcess.ProcessId is int pid ? $"GW2 open (pid {pid})" : "GW2 closed (idle)";
        EngineStatus status = engine.Status;
        return $"Heartbeat: {status.State}, mode {status.Mode}, context {status.Context}, foreground {focus.ForegroundName}, "
            + $"{running}, MumbleLink {mumble.Describe()}, cursor {(CursorProbe.IsCursorVisible() is bool v ? (v ? "visible" : "hidden") : "?")}"
            + $"{Environment.NewLine}              controller: {engine.DescribeInput()}; mouse sent in 10 s: ({dx}, {dy})"
            + $"{Environment.NewLine}              resources: {usage.Sample()}";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _heartbeat?.Dispose();
        _hidProbe?.Dispose();
        _gameInputProbe?.Dispose();
        _rawInputProbe?.Dispose();
        _preview?.Dispose();
        _tray?.Dispose();
        _overlay?.Dispose();
        _config?.Dispose();
        _keybinds?.Dispose();
        _engine?.Dispose();
        _rawGamepad?.Dispose();
        _allyButtons?.Dispose();
        _mumble?.Dispose();
        _gameProcess?.Dispose();
        _focus?.Dispose();
        _singleInstance?.Dispose();
        Log.Write("TyriaPad closed");
        Log.Close();
        base.OnExit(e);
    }
}

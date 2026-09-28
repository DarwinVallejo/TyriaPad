using System.Windows.Threading;

using TyriaPad.Core;
using TyriaPad.Core.Config;
using TyriaPad.Core.Diagnostics;
using TyriaPad.Core.Game;
using TyriaPad.Core.Keybinds;
using TyriaPad.Core.Overlay;

namespace TyriaPad.App.Overlay;

/// <summary>
/// Connects the engine, the GW2 window, MumbleLink, the configuration and the GW2 keybinds with the overlay window.
/// Every 200 ms it checks where the game's client area is and its interface size; the engine
/// notifies at once when the layer, the context or the radial changes. Everything runs on the UI thread.
/// </summary>
internal sealed class OverlayController : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private readonly TyriaPadEngine _engine;
    private readonly FocusWatcher _focus;
    private readonly MumbleLinkReader _mumble;
    private readonly ConfigStore _config;
    private readonly CalibrationStore _calibrations;
    private readonly InputBindsStore _keybinds;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly OverlayWindow _window = new();
    private readonly DispatcherTimer _timer;
    private OverlayState _state;
    private int _refreshScheduled;
    private bool _enabled;
    private WindowBounds? _bounds;
    private float _dpiScale = 1f;
    private UiSize _uiSize = UiSize.Normal;
    private UiState? _uiState;
    private string? _layoutKey;
    private SkillBarLayout? _layout;
    private SkillBarCalibration? _layoutCalibration;
    private int _layoutSlots = -1;
    private bool _calibrating;
    private bool _polled;
    private string? _lastHidden;

    public OverlayController(TyriaPadEngine engine, FocusWatcher focus, MumbleLinkReader mumble, ConfigStore config, CalibrationStore calibrations, InputBindsStore keybinds)
    {
        _engine = engine;
        _focus = focus;
        _mumble = mumble;
        _config = config;
        _calibrations = calibrations;
        _keybinds = keybinds;
        _state = engine.Overlay;
        _enabled = config.Current.Settings.Overlay.Enabled;

        _window.Calibration.Changed += () => Refresh();
        _window.Calibration.Saved += SaveCalibration;
        _window.Calibration.Cancelled += EndCalibration;

        engine.OverlayChanged += OnOverlayChanged;
        config.Changed += OnConfigChanged;
        keybinds.Changed += OnKeybindsChanged;
        _timer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, (_, _) => Poll(), _dispatcher);
        _timer.Start();
        Poll();
    }

    /// <summary>Raised when <see cref="Enabled"/> changes (from the tray or from config.json).</summary>
    public event Action<bool>? EnabledChanged;

    /// <summary>Show the overlay. Reloading config.json brings back the file's value.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            EnabledChanged?.Invoke(value);
            Refresh();
        }
    }

    public bool IsCalibrating => _calibrating;

    /// <summary>Key of the calibration in use (resolution and interface size), or null without a GW2 window.</summary>
    public string? CurrentKey => _bounds is { } b ? SkillBarLayout.KeyFor(b.Width, b.Height, _uiSize) : null;

    /// <summary>Starts calibrating. Returns false (without opening anything) if the GW2 window has not been seen yet.</summary>
    public bool StartCalibration()
    {
        Poll();
        if (_bounds is not { } bounds || CurrentKey is not { } key)
        {
            return false;
        }

        SkillBarCalibration defaults = SkillBarLayout.DefaultCalibration(bounds.Width, bounds.Height, _uiSize, _dpiScale);
        SkillBarCalibration current = _calibrations.Get(key) ?? defaults;
        _calibrating = true;
        _window.ClickThrough = false;
        _window.Place(bounds);
        _window.Calibration.Begin(current, defaults, _window.DpiScale, key);
        Log.Write($"Calibration started ({key}, DPI x{_window.DpiScale:F2})");
        Refresh();
        return true;
    }

    /// <summary>Deletes the saved calibration for the current resolution and interface size.</summary>
    public bool ResetCalibration()
    {
        if (CurrentKey is not { } key || !_calibrations.Remove(key))
        {
            return false;
        }

        Log.Write($"Calibration deleted ({key})");
        Refresh();
        return true;
    }

    public void Dispose()
    {
        _timer.Stop();
        _engine.OverlayChanged -= OnOverlayChanged;
        _config.Changed -= OnConfigChanged;
        _keybinds.Changed -= OnKeybindsChanged;
        _window.Close();
    }

    private void OnOverlayChanged(OverlayState state)
    {
        Volatile.Write(ref _state, state);
        if (Interlocked.Exchange(ref _refreshScheduled, 1) == 0)
        {
            _dispatcher.InvokeAsync(() =>
            {
                Interlocked.Exchange(ref _refreshScheduled, 0);
                Refresh();
            }, DispatcherPriority.Render);
        }
    }

    private void OnConfigChanged(LoadedConfig config)
        => _dispatcher.InvokeAsync(() =>
        {
            Enabled = config.Settings.Overlay.Enabled;
            Refresh();
        });

    // When the binds are exported again from GW2, the glyphs move to their new slots.
    private void OnKeybindsChanged(Gw2Keybinds keybinds) => _dispatcher.InvokeAsync(Refresh);

    private void Poll()
    {
        nint hwnd = _focus.GameWindow;
        WindowBounds? bounds = GameWindow.TryGetClientBounds(hwnd);
        float dpiScale = bounds is null ? _dpiScale : GameWindow.GetDpiScale(hwnd);
        UiSize uiSize = _mumble.GetUiSize() ?? _uiSize;
        UiState? uiState = _mumble.GetUiState();
        if (_polled && bounds == _bounds && dpiScale == _dpiScale && uiSize == _uiSize && uiState == _uiState)
        {
            return;
        }

        _polled = true;

        if (bounds != _bounds || dpiScale != _dpiScale)
        {
            Log.Write(bounds is { } b ? $"GW2 window: {b.Width}x{b.Height} at ({b.X}, {b.Y}), Windows at {dpiScale * 100:F0}%" : "GW2 window: not available");
        }

        if (uiSize != _uiSize)
        {
            Log.Write($"GW2 interface size: {uiSize}");
        }

        _bounds = bounds;
        _dpiScale = dpiScale;
        _uiSize = uiSize;
        _uiState = uiState;
        Refresh();
    }

    private void Refresh()
    {
        OverlayState state = Volatile.Read(ref _state);
        OverlaySettings settings = _config.Current.Settings.Overlay;

        if (_calibrating && _bounds is null)
        {
            EndCalibration();
        }

        string? hiddenReason = HiddenReason(state, settings);
        if (hiddenReason is not null)
        {
            if (_lastHidden != hiddenReason)
            {
                _lastHidden = hiddenReason;
                Log.Write($"Overlay hidden: {hiddenReason}");
            }

            _window.Canvas.Model = null;
            _window.HideOverlay();
            return;
        }

        if (_lastHidden is not null)
        {
            _lastHidden = null;
            Log.Write("Overlay visible");
        }

        WindowBounds bounds = _bounds!.Value;
        bool mapOpen = _uiState is { } ui && (ui & UiState.MapOpen) != 0;
        bool showSkillBar = _calibrating || !(mapOpen && settings.HideWhenMapOpen);
        _window.Canvas.Model = OverlayModelBuilder.Build(state, bounds, ResolveLayout(bounds, settings), settings, showSkillBar, _calibrating, keys: _keybinds.Current.SlotKeys);
        _window.Place(bounds);
    }

    private string? HiddenReason(OverlayState state, OverlaySettings settings)
    {
        if (!_enabled)
        {
            return "disabled";
        }

        if (_bounds is null)
        {
            return "no GW2 window";
        }

        if (_calibrating)
        {
            return null;
        }

        if (!state.IsActive)
        {
            return state.State switch
            {
                EngineState.Paused => "paused",
                EngineState.NoController => "no controller",
                EngineState.TextboxFocused => "typing",
                _ => "GW2 not focused",
            };
        }

        if (_uiState is null)
        {
            return "MumbleLink has no data (loading or character select)";
        }

        bool mapOpen = (_uiState.Value & UiState.MapOpen) != 0;
        if (mapOpen && settings.HideWhenMapOpen && state.Radial is null && settings.Indicator == IndicatorCorner.None)
        {
            return "map open";
        }

        return null;
    }

    private SkillBarLayout ResolveLayout(WindowBounds bounds, OverlaySettings settings)
    {
        string key = SkillBarLayout.KeyFor(bounds.Width, bounds.Height, _uiSize);
        SkillBarCalibration calibration = _calibrating
            ? _window.Calibration.Read()
            : _calibrations.Get(key) ?? SkillBarLayout.DefaultCalibration(bounds.Width, bounds.Height, _uiSize, _dpiScale);

        if (_layout is null || key != _layoutKey || calibration != _layoutCalibration || settings.ProfessionSlots != _layoutSlots)
        {
            _layoutKey = key;
            _layoutCalibration = calibration;
            _layoutSlots = settings.ProfessionSlots;
            _layout = SkillBarLayout.FromCalibration(calibration, settings.ProfessionSlots);
        }

        return _layout;
    }

    private void SaveCalibration(SkillBarCalibration calibration)
    {
        if (CurrentKey is { } key)
        {
            _calibrations.Set(key, calibration);
        }

        EndCalibration();
    }

    private void EndCalibration()
    {
        if (!_calibrating)
        {
            return;
        }

        _calibrating = false;
        _window.Calibration.End();
        _window.ClickThrough = true;
        Log.Write("Calibration finished");
        Refresh();
    }
}

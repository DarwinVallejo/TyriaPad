using System.Diagnostics;
using System.IO;
using System.Windows.Threading;

using TyriaPad.App.Overlay;
using TyriaPad.App.Settings;
using TyriaPad.Core;
using TyriaPad.Core.Config;
using TyriaPad.Core.Diagnostics;
using TyriaPad.Core.Input;
using TyriaPad.Core.Keybinds;
using TyriaPad.Core.Mapping;

using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TyriaPad.App;

/// <summary>Tray icon with "Pause", overlay, configuration and "Exit"; the tooltip shows the status.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TyriaPadEngine _engine;
    private readonly ConfigStore _config;
    private readonly OverlayController _overlay;
    private readonly InputBindsStore _keybinds;
    private readonly AllyVendorButtons _allyButtons;
    private readonly Func<IReadOnlyList<string>> _checkKeybinds;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Forms.ToolStripMenuItem _pauseItem = new("Pause") { CheckOnClick = true };
    private readonly Forms.ToolStripMenuItem _overlayItem = new("Show overlay") { CheckOnClick = true };
    private readonly Forms.ToolStripMenuItem _startupItem = new("Start with Windows") { CheckOnClick = true };
    private readonly Forms.NotifyIcon _icon;
    private bool _disposed;
    private bool _manualCheck;
    private IReadOnlyList<string> _lastIssues = [];
    private SettingsWindow? _settings;

    public TrayIcon(TyriaPadEngine engine, ConfigStore config, OverlayController overlay, InputBindsStore keybinds, AllyVendorButtons allyButtons, Func<IReadOnlyList<string>> checkKeybinds, Action exit)
    {
        _engine = engine;
        _config = config;
        _overlay = overlay;
        _keybinds = keybinds;
        _allyButtons = allyButtons;
        _checkKeybinds = checkKeybinds;
        _pauseItem.CheckedChanged += (_, _) => _engine.Paused = _pauseItem.Checked;
        _overlayItem.Checked = overlay.Enabled;
        _overlayItem.CheckedChanged += (_, _) => _overlay.Enabled = _overlayItem.Checked;
        _overlay.EnabledChanged += enabled => _overlayItem.Checked = enabled;
        var settingsItem = new Forms.ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings());
        settingsItem.Font = new Drawing.Font(settingsItem.Font, Drawing.FontStyle.Bold);
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_overlayItem);
        _menu.Items.Add("Calibrate overlay…", null, (_, _) => Calibrate());
        _menu.Items.Add("Reset calibration", null, (_, _) => ResetCalibration());
        _menu.Items.Add("Check GW2 keybinds", null, (_, _) => CheckKeybindsNow());
        _menu.Items.Add("Learn Ally M1/M2…", null, (_, _) => LearnAllyButtons());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Open configuration folder", null, (_, _) => Open(_config.Directory));
        _menu.Items.Add("Reload configuration", null, (_, _) => Reload());
        _menu.Items.Add("Open log", null, (_, _) => OpenLog());
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = AppIcon.CreateTrayIcon(),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenSettings();
        _menu.Opening += (_, _) => _startupItem.Checked = StartupShortcut.IsEnabled;
        _startupItem.Click += (_, _) => ToggleStartup();
        _engine.StatusChanged += OnStatusChanged;
        _config.Changed += OnConfigChanged;
        _keybinds.Changed += OnKeybindsChanged;
        UpdateText(_engine.Status);
        if (_config.LastError is { } error)
        {
            ShowError(error);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _engine.StatusChanged -= OnStatusChanged;
        _config.Changed -= OnConfigChanged;
        _keybinds.Changed -= OnKeybindsChanged;
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
        _menu.Dispose();
    }

    /// <summary>Opens the settings window (or brings it to the front if it is already open).</summary>
    public void OpenSettings()
    {
        if (_settings is { } open)
        {
            if (open.WindowState == System.Windows.WindowState.Minimized)
            {
                open.WindowState = System.Windows.WindowState.Normal;
            }

            open.Activate();
            return;
        }

        _settings = new SettingsWindow(_config, () => _keybinds.Current);
        _settings.Closed += (_, _) =>
        {
            _settings = null;
            MemoryTrim.After(TimeSpan.FromSeconds(2), "settings window closed");
        };
        _settings.Show();
        _settings.Activate();
    }

    private void ToggleStartup()
    {
        bool enabled = _startupItem.Checked;
        if (!StartupShortcut.Set(enabled))
        {
            _startupItem.Checked = !enabled;
            _icon.ShowBalloonTip(5000, "TyriaPad", "Could not change Start with Windows (see the log).", Forms.ToolTipIcon.Warning);
            return;
        }

        _icon.ShowBalloonTip(4000, "TyriaPad", enabled
            ? "It will open when Windows starts and wait idle until you start GW2."
            : "It will no longer open when Windows starts.", Forms.ToolTipIcon.Info);
    }

    private static void OpenLog()
    {
        if (Log.FilePath is { } path && File.Exists(path))
        {
            Open(path);
        }
    }

    private static void Open(string path)
        => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    private void Calibrate()
    {
        if (_overlay.IsCalibrating)
        {
            return;
        }

        if (!_overlay.StartCalibration())
        {
            _icon.ShowBalloonTip(5000, "TyriaPad", "Start Guild Wars 2 and bring it to the foreground once before calibrating.", Forms.ToolTipIcon.Info);
        }
    }

    /// <summary>Asks for M1 and then M2 with tray notifications; the result is saved on its own to allybuttons.json.</summary>
    private void LearnAllyButtons()
    {
        if (_allyButtons.StartLearning(learner => _dispatcher.InvokeAsync(() => OnLearnProgress(learner))) is not { } learner)
        {
            _icon.ShowBalloonTip(5000, "TyriaPad: M1/M2", "The ASUS HID collection was not found. This only works on the ROG Ally (see the log).", Forms.ToolTipIcon.Warning);
            return;
        }

        OnLearnProgress(learner);
    }

    private void OnLearnProgress(AllyButtonLearner learner)
    {
        if (_disposed)
        {
            return;
        }

        switch (learner.Status)
        {
            case LearnStatus.Waiting:
                _icon.ShowBalloonTip(20000, "TyriaPad: M1/M2", $"Hold {learner.Current} for a second and release it.", Forms.ToolTipIcon.Info);
                break;
            case LearnStatus.Done:
                string codes = string.Join(", ", learner.Learned.Select(static c => $"{c.Key} = {c.Value}"));
                _icon.ShowBalloonTip(5000, "TyriaPad: M1/M2", $"Done: {codes}. They can now be used in the profile.", Forms.ToolTipIcon.Info);
                break;
            case LearnStatus.SameAsPrevious:
                _icon.ShowBalloonTip(10000, "TyriaPad: M1/M2", $"{learner.Current} sends the same as {learner.SameAs}: the Ally does not tell them apart this way. Both will do what {learner.SameAs} does.", Forms.ToolTipIcon.Warning);
                break;
            case LearnStatus.TimedOut:
                _icon.ShowBalloonTip(8000, "TyriaPad: M1/M2", $"Nothing arrived from {learner.Current}. In Armoury Crate SE, M1/M2 must not be disabled or have anything assigned. The previous codes are kept.", Forms.ToolTipIcon.Warning);
                break;
        }
    }

    private void ResetCalibration()
    {
        string message = _overlay.ResetCalibration()
            ? $"Calibration deleted ({_overlay.CurrentKey}). The default positions are used."
            : "There was no saved calibration for this resolution and interface size.";
        _icon.ShowBalloonTip(4000, "TyriaPad", message, Forms.ToolTipIcon.Info);
    }

    private void Reload()
    {
        // If it goes well, Changed notifies with its own tray notification.
        _config.Reload();
        if (_config.LastError is { } error)
        {
            ShowError(error);
        }
    }

    private void ShowError(string error)
    {
        // The tray notification has little room: the full detail stays in the log.
        string first = error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? error;
        _icon.ShowBalloonTip(8000, "TyriaPad: configuration error", $"{first}\nThe previous one is kept. Details in the log.", Forms.ToolTipIcon.Warning);
    }

    private void OnConfigChanged(LoadedConfig config)
        => _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                _icon.ShowBalloonTip(2000, "TyriaPad", $"Configuration reloaded: {config.Profile.Name}", Forms.ToolTipIcon.Info);
            }
        });

    /// <summary>
    /// Reads the XML again (in case the folder notification did not arrive) and shows the list. The reload
    /// notifies through Changed on this same thread, so the overlay also updates.
    /// </summary>
    private void CheckKeybindsNow()
    {
        _manualCheck = true;
        try
        {
            _keybinds.Reload();
        }
        finally
        {
            _manualCheck = false;
        }

        ShowKeybindIssues(_lastIssues);
    }

    // The binds were exported again: the overlay already updates on its own; here it only notifies.
    private void OnKeybindsChanged(Gw2Keybinds keybinds)
    {
        IReadOnlyList<string> issues = _checkKeybinds();
        _lastIssues = issues;
        if (_manualCheck)
        {
            return;
        }

        _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                string source = keybinds.Source is { } path ? Path.GetFileName(path) : "default keys";
                string summary = issues.Count == 0 ? "no warnings" : $"{issues.Count} warnings (\"Check GW2 keybinds\")";
                _icon.ShowBalloonTip(3000, "TyriaPad: GW2 keybinds", $"{source}: {summary}", issues.Count == 0 ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
            }
        });
    }

    /// <summary>List of warnings in a message box (the user asks for it from the tray, so it may steal focus).</summary>
    private void ShowKeybindIssues(IReadOnlyList<string> issues)
    {
        const int MaxShown = 20;
        string source = _keybinds.Current.Source ?? "no XML: GW2's default keys are assumed";
        string body = issues.Count == 0
            ? "Everything matches: every profile key does something in GW2 and every skill has a button."
            : string.Join(Environment.NewLine, issues.Take(MaxShown).Select(static i => "• " + i))
                + (issues.Count > MaxShown ? $"{Environment.NewLine}… and {issues.Count - MaxShown} more (see the log)" : string.Empty);
        System.Windows.MessageBox.Show(
            $"Keybinds: {source}{Environment.NewLine}{Environment.NewLine}{body}",
            "TyriaPad: GW2 keybinds",
            System.Windows.MessageBoxButton.OK,
            issues.Count == 0 ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
    }

    private void OnStatusChanged(EngineStatus status) => _dispatcher.InvokeAsync(() => UpdateText(status));

    private void UpdateText(EngineStatus status)
    {
        if (!_disposed)
        {
            // NotifyIcon.Text allows 127 characters at most.
            string text = $"TyriaPad: {Describe(status)}";
            _icon.Text = text.Length > 127 ? text[..127] : text;
        }
    }

    private static string Describe(EngineStatus status) => status.State switch
    {
        EngineState.Active => $"active ({DescribeContext(status)}) - {status.ProfileName}",
        EngineState.Paused => "paused",
        EngineState.NoController => "no controller",
        EngineState.WaitingForGame => "waiting for Guild Wars 2",
        EngineState.TextboxFocused => "typing (blocked)",
        _ => status.State.ToString(),
    };

    private static string DescribeContext(EngineStatus status) => status.Context switch
    {
        GameContext.Pointer => "cursor",
        GameContext.Mount => "mount",
        _ => status.Mode == CameraMode.Pointer ? "cursor" : "camera",
    };
}

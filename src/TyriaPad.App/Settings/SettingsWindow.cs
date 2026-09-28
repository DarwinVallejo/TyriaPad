using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using TyriaPad.Core.Config;
using TyriaPad.Core.Keybinds;
using TyriaPad.Core.Mapping;

namespace TyriaPad.App.Settings;

/// <summary>
/// TyriaPad settings: general, controller, overlay, buttons (layer editor) and radials. Nothing is
/// applied until "Save": then config.json (only what changed, keeping its comments) and the
/// profile are written, and TyriaPad hot-reloads them as if they had been edited by hand.
/// The default profile is not overwritten: the first time it's saved, a name is asked for the copy.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly ConfigStore _config;
    private readonly Func<Gw2Keybinds> _keybinds;
    private readonly TabControl _tabs = new() { Margin = new Thickness(8, 8, 8, 0) };
    private readonly ProfileEditor _profileEditor;
    private readonly RadialEditor _radialEditor;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxHeight = 60 };
    private readonly Button _save;
    private readonly TextBox _profileTitle = new() { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
    private TyriaPadSettings _saved;
    private TyriaPadSettings _settings;
    private bool _startWithWindows;
    private bool _startWithWindowsSaved;
    private bool _profileDirty;
    private string _profileName;

    public SettingsWindow(ConfigStore config, Func<Gw2Keybinds> keybinds)
    {
        _config = config;
        _keybinds = keybinds;
        _saved = _settings = config.Current.Settings;
        _profileName = _settings.Profile;
        _startWithWindows = _startWithWindowsSaved = StartupShortcut.IsEnabled;

        Title = "TyriaPad: settings";
        Icon = AppIcon.Image;
        Width = 1240;
        Height = 780;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ApplyTheme(this);

        // On the Ally's 7" screen (1280x720 at 150%) it opens maximized.
        if (SystemParameters.WorkArea.Width < Width || SystemParameters.WorkArea.Height < Height)
        {
            WindowState = WindowState.Maximized;
        }

        _profileEditor = new ProfileEditor(this, () => _keybinds());
        _profileEditor.Changed += ProfileChanged;
        _radialEditor = new RadialEditor(this);
        _radialEditor.Changed += ProfileChanged;
        _radialEditor.RadialsChanged += () => _profileEditor.RefreshAll();

        _profileTitle.TextChanged += (_, _) =>
        {
            if (_profileEditor.Draft.Name != _profileTitle.Text)
            {
                _profileEditor.Draft.Name = _profileTitle.Text;
                ProfileChanged();
            }
        };
        _save = Ui.Button("Save", () => Save(), accent: true);
        var bottom = new DockPanel { Margin = new Thickness(16, 10, 16, 14) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_save);
        buttons.Children.Add(Ui.Button("Close", Close));
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(_status);

        var root = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(_tabs);
        Content = root;

        BuildSettingsTabs();
        _tabs.Items.Add(new TabItem { Header = "Buttons", Content = _profileEditor });
        _tabs.Items.Add(new TabItem { Header = "Radials", Content = _radialEditor });
        LoadProfile(_profileName);
        Validate();
    }

    private bool IsDirty => _profileDirty || _settings != _saved || _startWithWindows != _startWithWindowsSaved;

    /// <summary>Windows 11 Fluent theme (light or dark following the system), also comfortable for touch.</summary>
    public static void ApplyTheme(Window window)
    {
#pragma warning disable WPF0001 // ThemeMode is experimental in WPF, but stable for what it's used for here.
        window.ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
    }

    /// <summary>Saves each tab as a PNG, to review the design without opening the window (diagnostics).</summary>
    public static void Snapshot(ConfigStore config, string directory)
    {
        Directory.CreateDirectory(directory);
        var window = new SettingsWindow(config, () => Gw2Keybinds.Defaults)
        {
            WindowState = WindowState.Normal,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            Width = 1280,
            Height = 720,
            ShowActivated = false,
        };
        window.Show();
        void Capture(string name)
        {
            window.Dispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
            var visual = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (DrawingContext dc = background.RenderOpen())
            {
                dc.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
            }

            bitmap.Render(background);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream file = File.Create(Path.Combine(directory, $"settings-{name}.png"));
            encoder.Save(file);
        }

        for (int i = 0; i < window._tabs.Items.Count; i++)
        {
            window._tabs.SelectedIndex = i;
            Capture($"{i}-{((TabItem)window._tabs.Items[i]).Header}");
        }

        window._tabs.SelectedItem = window._tabs.Items.OfType<TabItem>().First(t => t.Content == window._profileEditor);
        window._profileEditor.Show("mount", Core.Input.GamepadButtons.LeftBumper, Core.Input.GamepadButtons.X);
        Capture("5-mount-LB");
        window._profileEditor.Show("pointer", Core.Input.GamepadButtons.None, Core.Input.GamepadButtons.LeftBumper);
        Capture("6-cursor");
        window._profileEditor.Show("combat", Core.Input.GamepadButtons.None, Core.Input.GamepadButtons.LeftStick);
        Capture("7-L3");

        window._profileDirty = false;
        window._settings = window._saved;
        window._startWithWindows = window._startWithWindowsSaved;
        window.Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!IsDirty)
        {
            return;
        }

        MessageBoxResult answer = Dialogs.Ask(this, "TyriaPad", "There are unsaved changes. Save them?", MessageBoxButton.YesNoCancel);
        e.Cancel = answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !Save());
    }

    private void BuildSettingsTabs()
    {
        _tabs.Items.Add(new TabItem { Header = "General", Content = GeneralPage() });
        _tabs.Items.Add(new TabItem { Header = "Controller", Content = ControllerPage() });
        _tabs.Items.Add(new TabItem { Header = "Overlay", Content = OverlayPage() });
    }

    /// <summary>Applies a change to the settings being edited (the records are immutable).</summary>
    private void Set(Func<TyriaPadSettings, TyriaPadSettings> change)
    {
        _settings = change(_settings);
        Validate();
    }

    private ScrollViewer GeneralPage()
    {
        // When the page is rebuilt, the name box is reused: it has to be taken out of the previous one.
        (_profileTitle.Parent as Panel)?.Children.Remove(_profileTitle);
        IReadOnlyList<string> profiles = _config.ListProfiles();
        ComboBox profile = null!;
        profile = Ui.Combo(profiles.Select(static p => (p, p)), _settings.Profile, name =>
        {
            if (string.Equals(name, _profileName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_profileDirty && Dialogs.Ask(this, "Switch profile", $"There are unsaved changes in \"{_profileName}\". Discard them and open \"{name}\"?") != MessageBoxResult.Yes)
            {
                // Show again the profile that is still open.
                profile.SelectedItem = profile.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, _profileName, StringComparison.OrdinalIgnoreCase));
                return;
            }

            Set(s => s with { Profile = name });
            LoadProfile(name);
        });

        ComboBox actionCamera = Ui.Combo(
            KeyNames.EditorKeys.Select(static k => (KeyNames.ProfileName(k), KeyNames.ProfileName(k))),
            KeyNames.TryParseKey(_settings.ActionCameraKey, out Core.Output.Key current) ? KeyNames.ProfileName(current) : _settings.ActionCameraKey,
            key => Set(s => s with { ActionCameraKey = key }));
        actionCamera.MaxDropDownHeight = 420;

        var binds = new List<(string, string)> { ("", "The most recently exported"), (InputBindsStore.Disabled, "None: assume GW2's default keys") };
        try
        {
            if (Directory.Exists(InputBindsStore.DefaultDirectory))
            {
                binds.AddRange(Directory.EnumerateFiles(InputBindsStore.DefaultDirectory, "*.xml").Select(static f => (Path.GetFileName(f), Path.GetFileName(f))));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        if (!binds.Any(b => string.Equals(b.Item1, _settings.InputBinds, StringComparison.OrdinalIgnoreCase)))
        {
            binds.Add((_settings.InputBinds, _settings.InputBinds));
        }

        return Ui.Page(
            Ui.Heading("Profile"),
            Ui.Row("Active profile", profile, "What each button does. Edited in the Buttons and Radials tabs."),
            Ui.Row("Profile name", _profileTitle, "The one shown in the tray. The file is profiles/<active profile>.json."),
            Ui.Heading("Guild Wars 2"),
            Ui.Row("Action Camera key", actionCamera, "The one bound in GW2 to \"Toggle Action Camera\" (Options → Control Options). The game has none by default."),
            Ui.Row("GW2 keybinds", Ui.Combo(binds, _settings.InputBinds, v => Set(s => s with { InputBinds = v })),
                "XML that GW2 exports to Documents\\Guild Wars 2\\InputBinds. It places the glyphs and warns about keys with no action. After changing a bind in the game you have to export again."),
            Ui.Heading("Startup"),
            Ui.Check("Start with Windows", _startWithWindows, v =>
            {
                _startWithWindows = v;
                Validate();
            }, "Creates a shortcut in the Windows Startup folder (nothing in the registry). Without GW2 open, TyriaPad stays idle and barely reads the controller. If you delete the program folder, turn this option off first."),
            Ui.Check("Close TyriaPad when GW2 closes", _settings.ExitWithGame, v => Set(s => s with { ExitWithGame = v }),
                "For those who open it by hand before playing. It doesn't apply when Windows startup opened it: then it keeps waiting for the next session."));
    }

    private ScrollViewer ControllerPage()
    {
        TyriaPadSettings s = _settings;
        return Ui.Page(
            Ui.Heading("Camera (Action Camera)"),
            Ui.Slider("Speed", 200, 5000, 50, "0", s.Camera.Speed, v => Set(x => x with { Camera = x.Camera with { Speed = (float)v } }), "Pixels per second with the stick fully pushed."),
            Ui.Slider("Curve", 1, 4, 0.1, "0.0", s.Camera.Exponent, v => Set(x => x with { Camera = x.Camera with { Exponent = (float)v } }), "1 = linear; higher = more precision near the center."),
            Ui.Heading("Cursor (menus)"),
            Ui.Slider("Speed", 200, 4000, 50, "0", s.Pointer.Speed, v => Set(x => x with { Pointer = x.Pointer with { Speed = (float)v } })),
            Ui.Slider("Curve", 1, 4, 0.1, "0.0", s.Pointer.Exponent, v => Set(x => x with { Pointer = x.Pointer with { Exponent = (float)v } })),
            Ui.Heading("Sticks and triggers"),
            Ui.Slider("Left stick deadzone", 0, 0.6, 0.005, "0.000", s.Input.LeftStickDeadzone, v => Set(x => x with { Input = x.Input with { LeftStickDeadzone = (float)v } })),
            Ui.Slider("Right stick deadzone", 0, 0.6, 0.005, "0.000", s.Input.RightStickDeadzone, v => Set(x => x with { Input = x.Input with { RightStickDeadzone = (float)v } })),
            Ui.Slider("Trigger: press from", 0.05, 0.95, 0.05, "0.00", s.Input.TriggerPressThreshold, v => Set(x => x with { Input = x.Input with { TriggerPressThreshold = (float)v } })),
            Ui.Slider("Trigger: release below", 0, 0.9, 0.05, "0.00", s.Input.TriggerReleaseThreshold, v => Set(x => x with { Input = x.Input with { TriggerReleaseThreshold = (float)v } })),
            Ui.Slider("Movement: press from", 0.1, 1, 0.05, "0.00", s.Movement.PressThreshold, v => Set(x => x with { Movement = x.Movement with { PressThreshold = (float)v } }), "How far the stick has to tilt for W/A/S/D to be pressed."),
            Ui.Slider("Movement: release below", 0, 0.9, 0.05, "0.00", s.Movement.ReleaseThreshold, v => Set(x => x with { Movement = x.Movement with { ReleaseThreshold = (float)v } })),
            Ui.Heading("Gestures"),
            Ui.Slider("Hold from (ms)", 100, 1000, 10, "0", s.Gestures.HoldMs, v => Set(x => x with { Gestures = x.Gestures with { HoldMs = (int)v } })),
            Ui.Slider("Double tap: maximum time (ms)", 100, 600, 10, "0", s.Gestures.DoubleTapMs, v => Set(x => x with { Gestures = x.Gestures with { DoubleTapMs = (int)v } })),
            Ui.Slider("Resync the mode (ms)", 500, 4000, 50, "0", s.Gestures.ResyncMs, v => Set(x => x with { Gestures = x.Gestures with { ResyncMs = (int)v } }), "Holding the Action Camera button this long only flips TyriaPad's mode, without sending a key."),
            Ui.Slider("Wheel repeat (ms)", 50, 1000, 10, "0", s.Gestures.WheelRepeatMs, v => Set(x => x with { Gestures = x.Gestures with { WheelRepeatMs = (int)v } })),
            Ui.Heading("Mode switching"),
            Ui.Check("Follow the Windows cursor (visible = cursor, hidden = camera)", s.FollowCursor, v => Set(x => x with { FollowCursor = v })),
            Ui.Slider("Cursor debounce (ms)", 20, 500, 10, "0", s.CursorDebounceMs, v => Set(x => x with { CursorDebounceMs = (int)v })),
            Ui.Slider("Wait after toggling by hand (ms)", 100, 2000, 50, "0", s.ManualGraceMs, v => Set(x => x with { ManualGraceMs = (int)v })),
            Ui.Check("Cursor context with the map open", s.PointerWhenMapOpen, v => Set(x => x with { PointerWhenMapOpen = v })),
            Ui.Heading("Xbox full screen experience"),
            Ui.Check("Read the controller through Raw Input if XInput reports idle", s.Input.RawInputFallback, v => Set(x => x with { Input = x.Input with { RawInputFallback = v } }),
                "In the Xbox full screen experience, Windows doesn't give XInput to background apps and this is the way that works. There LT and RT share an axis: pressed together they cancel out, so the LT+RT layer doesn't activate (use LT + D-pad for F5–F8)."),
            Ui.Heading("ROG Ally X: M1/M2"),
            Ui.Check("Read M1/M2", s.AllyButtons.Enabled, v => Set(x => x with { AllyButtons = x.AllyButtons with { Enabled = v } }), "Their codes are learned from the tray → \"Learn Ally M1/M2…\". In Armoury Crate SE they must be left unassigned."),
            Ui.Slider("Release after (ms) without reports", 50, 500, 10, "0", s.AllyButtons.ReleaseAfterMs, v => Set(x => x with { AllyButtons = x.AllyButtons with { ReleaseAfterMs = (int)v } })));
    }

    private ScrollViewer OverlayPage()
    {
        OverlaySettings o = _settings.Overlay;
        return Ui.Page(
            Ui.Heading("Overlay"),
            Ui.Check("Show the overlay", o.Enabled, v => Set(x => x with { Overlay = x.Overlay with { Enabled = v } })),
            Ui.Slider("Glyph size", 0.5, 2, 0.05, "0.00", o.Scale, v => Set(x => x with { Overlay = x.Overlay with { Scale = (float)v } })),
            Ui.Slider("Opacity", 0.2, 1, 0.05, "0.00", o.Opacity, v => Set(x => x with { Overlay = x.Overlay with { Opacity = (float)v } })),
            Ui.Slider("Profession slots (F1…Fn)", 0, 8, 1, "0", o.ProfessionSlots, v => Set(x => x with { Overlay = x.Overlay with { ProfessionSlots = (int)v } })),
            Ui.Check("Hide with the map open", o.HideWhenMapOpen, v => Set(x => x with { Overlay = x.Overlay with { HideWhenMapOpen = v } })),
            Ui.Row("Mode indicator", Ui.Combo(
                [
                    (IndicatorCorner.TopRight, "Top right"),
                    (IndicatorCorner.TopLeft, "Top left"),
                    (IndicatorCorner.BottomRight, "Bottom right"),
                    (IndicatorCorner.BottomLeft, "Bottom left"),
                    (IndicatorCorner.None, "Don't show it"),
                ],
                o.Indicator,
                v => Set(x => x with { Overlay = x.Overlay with { Indicator = v } }))),
            Ui.Slider("Radial radius (px)", 60, 300, 5, "0", o.RadialRadius, v => Set(x => x with { Overlay = x.Overlay with { RadialRadius = (float)v } })),
            Ui.Hint("The skill bar position is adjusted from the tray → \"Calibrate overlay…\", with GW2 open."));
    }

    private void LoadProfile(string name)
    {
        ProfileDraft draft;
        try
        {
            draft = ProfileDraft.FromJson(_config.ReadProfile(name));
        }
        catch (Exception ex) when (ex is ConfigException or IOException or UnauthorizedAccessException)
        {
            // A profile with syntax errors can't be edited here: the one in use is opened.
            _status.Text = $"Can't open \"{name}\" in the editor: {ex.Message}";
            draft = ProfileDraft.FromJson(Defaults.ProfileJson);
            name = Defaults.ProfileName;
        }

        _profileName = name;
        _profileDirty = false;
        _profileEditor.Load(draft);
        _radialEditor.Load(draft);
        _profileTitle.Text = draft.Name;
        _profileDirty = false;
        Validate();
    }

    private void ProfileChanged()
    {
        _profileDirty = true;
        Validate();
    }

    /// <summary>Checks settings and profile on every change; the first error shows at the bottom and blocks "Save".</summary>
    private bool Validate()
    {
        var errors = new List<string>(_settings.Validate());
        try
        {
            _profileEditor?.Draft.Validate();
        }
        catch (ConfigException ex)
        {
            errors.AddRange(ex.Message.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(static e => "profile: " + e));
        }

        if (_save is null)
        {
            return errors.Count == 0;
        }

        _save.IsEnabled = errors.Count == 0 && IsDirty;
        _status.Foreground = errors.Count == 0 ? (Brush?)TryFindResource("TextFillColorSecondaryBrush") ?? Brushes.Gray : Brushes.IndianRed;
        _status.Text = errors.Count switch
        {
            0 => IsDirty ? "Unsaved changes." : $"Profile \"{_profileName}\". Changes apply on save, without restarting.",
            1 => errors[0],
            _ => $"{errors[0]}  (and {errors.Count - 1} more)",
        };
        return errors.Count == 0;
    }

    private bool Save()
    {
        if (!Validate())
        {
            return false;
        }

        try
        {
            TyriaPadSettings settings = _settings;
            if (_profileDirty)
            {
                string target = _profileName;
                if (string.Equals(target, Defaults.ProfileName, StringComparison.OrdinalIgnoreCase))
                {
                    // The default profile stays intact (with its comments): the changes go to a copy.
                    string? chosen = Dialogs.Prompt(this, "Save as new profile",
                        $"\"{Defaults.ProfileName}\" is the default profile and is not overwritten. What name do you want to save your version under? It will be used from now on.",
                        "my-profile", ValidateProfileName);
                    if (chosen is null)
                    {
                        return false;
                    }

                    target = chosen;
                }

                _config.SaveProfile(target, _profileEditor.Draft.ToJson());
                _profileName = target;
                settings = settings with { Profile = target };
                if (_tabs.Items[0] is TabItem general)
                {
                    // The profile list in "General" has to include the new copy.
                    general.Content = null;
                    _settings = settings;
                    general.Content = GeneralPage();
                }
            }

            _config.SaveSettings(settings);
            bool startupFailed = _startWithWindows != _startWithWindowsSaved && !StartupShortcut.Set(_startWithWindows);
            _startWithWindowsSaved = StartupShortcut.IsEnabled;
            _startWithWindows = _startWithWindowsSaved;

            _settings = _saved = settings;
            _profileDirty = false;
            Validate();
            _status.Text = $"Saved. TyriaPad is already using the changes (profile \"{_profileName}\").";
            if (startupFailed)
            {
                _status.Text += " Could not change \"Start with Windows\" (see the log).";
            }

            return true;
        }
        catch (Exception ex) when (ex is ConfigException or IOException or UnauthorizedAccessException or FormatException)
        {
            _status.Foreground = Brushes.IndianRed;
            _status.Text = "Could not save: " + ex.Message;
            return false;
        }
    }

    private string? ValidateProfileName(string name)
    {
        if (name.Length == 0)
        {
            return "Type a name.";
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.StartsWith('.'))
        {
            return "The name can't contain \\ / : * ? \" < > |.";
        }

        if (string.Equals(name, Defaults.ProfileName, StringComparison.OrdinalIgnoreCase))
        {
            return "That's the default profile; pick another name.";
        }

        return _config.ListProfiles().Contains(name, StringComparer.OrdinalIgnoreCase) ? "A profile with that name already exists." : null;
    }
}

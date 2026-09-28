using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;

using Key = TyriaPad.Core.Output.Key;
using MouseButton = TyriaPad.Core.Output.MouseButton;
using WpfKey = System.Windows.Input.Key;

namespace TyriaPad.App.Settings;

/// <summary>
/// Picks a profile action without typing its syntax: type (key, click, wheel…) and its value.
/// The key is picked from a list (the Ally usually has no keyboard) or captured by pressing it.
/// <see cref="Changed"/> gives the action text, or null with "None".
/// </summary>
internal sealed class ActionPicker : WrapPanel
{
    private enum Kind
    {
        None,
        Key,
        Click,
        Wheel,
        ActionCamera,
        Mode,
        Radial,
    }

    private readonly Func<IEnumerable<string>> _radials;
    private readonly bool _allowRadial;
    private readonly ComboBox _kind = new() { Width = 150, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _value = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
    private readonly CheckBox _shift = Modifier("Shift");
    private readonly CheckBox _ctrl = Modifier("Ctrl");
    private readonly CheckBox _alt = Modifier("Alt");
    private readonly ComboBox _key = new() { Width = 120, Margin = new Thickness(0, 0, 8, 0), MaxDropDownHeight = 420 };
    private readonly ToggleButton _capture = new() { Content = "Capture", Padding = new Thickness(10, 4, 10, 4), ToolTip = "Press this button and then the key (with Shift/Ctrl/Alt if needed)" };
    private readonly ComboBox _option = new() { Width = 160 };
    private bool _updating;

    public ActionPicker(bool allowRadial, Func<IEnumerable<string>> radials)
    {
        _allowRadial = allowRadial;
        _radials = radials;
        Orientation = Orientation.Horizontal;

        AddKind(Kind.None, "None");
        AddKind(Kind.Key, "Key");
        AddKind(Kind.Click, "Click");
        AddKind(Kind.Wheel, "Wheel");
        AddKind(Kind.ActionCamera, "Action Camera");
        AddKind(Kind.Mode, "Change mode");
        if (allowRadial)
        {
            AddKind(Kind.Radial, "Radial");
        }

        foreach (Key key in KeyNames.EditorKeys)
        {
            _key.Items.Add(new ComboBoxItem { Content = KeyNames.ProfileName(key), Tag = key });
        }

        Children.Add(_kind);
        Children.Add(_value);
        _kind.SelectionChanged += (_, _) =>
        {
            if (!_updating)
            {
                ShowValueControls(SelectedKind, keepSelection: false);
                Raise();
            }
        };
        _shift.Click += (_, _) => Raise();
        _ctrl.Click += (_, _) => Raise();
        _alt.Click += (_, _) => Raise();
        _key.SelectionChanged += (_, _) => Raise();
        _option.SelectionChanged += (_, _) => Raise();
        _capture.PreviewKeyDown += OnCaptureKey;
        _capture.LostKeyboardFocus += (_, _) => _capture.IsChecked = false;
        _capture.Checked += (_, _) => _capture.Content = "Press a key…";
        _capture.Unchecked += (_, _) => _capture.Content = "Capture";
        ShowValueControls(Kind.None, keepSelection: false);
    }

    public event Action<string?>? Changed;

    /// <summary>Current action in the profile syntax, or null.</summary>
    public string? Value => Build();

    public void SetValue(string? action)
    {
        _updating = true;
        try
        {
            Kind kind = Kind.None;
            BindingAction? parsed = null;
            if (action is not null && ActionNames.TryParse(action, out parsed, out _))
            {
                kind = parsed switch
                {
                    ChordAction { Chord.Action.Kind: OutputKind.Mouse } => Kind.Click,
                    ChordAction => Kind.Key,
                    WheelAction => Kind.Wheel,
                    ActionCameraToggleAction => Kind.ActionCamera,
                    SetModeAction => Kind.Mode,
                    RadialAction when _allowRadial => Kind.Radial,
                    _ => Kind.None,
                };
            }

            SelectKind(kind);
            ShowValueControls(kind, keepSelection: false);
            switch (parsed)
            {
                case ChordAction { Chord: var chord }:
                    _shift.IsChecked = (chord.Modifiers & KeyModifiers.Shift) != 0;
                    _ctrl.IsChecked = (chord.Modifiers & KeyModifiers.Ctrl) != 0;
                    _alt.IsChecked = (chord.Modifiers & KeyModifiers.Alt) != 0;
                    if (chord.Action.Kind == OutputKind.Mouse)
                    {
                        SelectOption(chord.Action.Button);
                    }
                    else
                    {
                        SelectKey(chord.Action.Key);
                    }

                    break;
                case WheelAction wheel:
                    SelectOption(wheel.Direction > 0 ? "up" : "down");
                    break;
                case SetModeAction mode:
                    SelectOption(mode.ToString());
                    break;
                case RadialAction radial:
                    SelectOption(_option.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, radial.Name, StringComparison.OrdinalIgnoreCase))?.Tag ?? radial.Name);
                    break;
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private Kind SelectedKind => _kind.SelectedItem is ComboBoxItem { Tag: Kind kind } ? kind : Kind.None;

    private static CheckBox Modifier(string text) => new() { Content = text, Margin = new Thickness(0, 0, 10, 0), MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };

    private void AddKind(Kind kind, string text) => _kind.Items.Add(new ComboBoxItem { Content = text, Tag = kind });

    private void SelectKind(Kind kind)
        => _kind.SelectedItem = _kind.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (Kind)i.Tag == kind) ?? _kind.Items[0];

    private void SelectKey(Key key)
        => _key.SelectedItem = _key.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (Key)i.Tag == key);

    private void SelectOption(object tag)
    {
        ComboBoxItem? item = _option.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, tag));
        if (item is null && tag is string missing)
        {
            // A radial that no longer exists: shown so the error is visible on validation.
            item = new ComboBoxItem { Content = missing + " (does not exist)", Tag = missing };
            _option.Items.Add(item);
        }

        _option.SelectedItem = item;
    }

    private void ShowValueControls(Kind kind, bool keepSelection)
    {
        bool wasUpdating = _updating;
        _updating = true;
        try
        {
            _value.Children.Clear();
            if (!keepSelection)
            {
                _option.Items.Clear();
                _shift.IsChecked = _ctrl.IsChecked = _alt.IsChecked = false;
            }

            switch (kind)
            {
                case Kind.Key:
                    _value.Children.Add(_key);
                    _value.Children.Add(_shift);
                    _value.Children.Add(_ctrl);
                    _value.Children.Add(_alt);
                    _value.Children.Add(_capture);
                    if (_key.SelectedItem is null)
                    {
                        _key.SelectedIndex = 0;
                    }

                    break;
                case Kind.Click:
                    AddOptions(("Left", MouseButton.Left), ("Right", MouseButton.Right), ("Middle", MouseButton.Middle));
                    _value.Children.Add(_option);
                    _value.Children.Add(_shift);
                    _value.Children.Add(_ctrl);
                    _value.Children.Add(_alt);
                    break;
                case Kind.Wheel:
                    AddOptions(("Up", "up"), ("Down", "down"));
                    _value.Children.Add(_option);
                    break;
                case Kind.Mode:
                    AddOptions(("Toggle", "mode:toggle"), ("Cursor", "mode:pointer"), ("Camera", "mode:camera"));
                    _value.Children.Add(_option);
                    break;
                case Kind.Radial:
                    AddOptions(_radials().Select(static r => (r, (object)r)).ToArray());
                    _value.Children.Add(_option);
                    break;
                case Kind.ActionCamera:
                    _value.Children.Add(Ui.Hint("Sends the Action Camera key and toggles camera ↔ cursor"));
                    break;
            }

            if (_option.Items.Count > 0 && _option.SelectedItem is null)
            {
                _option.SelectedIndex = 0;
            }
        }
        finally
        {
            _updating = wasUpdating;
        }
    }

    private void AddOptions(params (string Text, object Tag)[] options)
    {
        _option.Items.Clear();
        foreach ((string text, object tag) in options)
        {
            _option.Items.Add(new ComboBoxItem { Content = text, Tag = tag });
        }
    }

    private string? Build()
    {
        string Modifiers() => (_shift.IsChecked == true ? "Shift+" : "") + (_ctrl.IsChecked == true ? "Ctrl+" : "") + (_alt.IsChecked == true ? "Alt+" : "");
        object? option = (_option.SelectedItem as ComboBoxItem)?.Tag;
        return SelectedKind switch
        {
            Kind.Key when _key.SelectedItem is ComboBoxItem { Tag: Key key } => Modifiers() + KeyNames.ProfileName(key),
            Kind.Click when option is MouseButton button => Modifiers() + "click:" + button.ToString().ToLowerInvariant(),
            Kind.Wheel when option is string direction => "wheel:" + direction,
            Kind.ActionCamera => ActionNames.ActionCamera,
            Kind.Mode when option is string mode => mode,
            Kind.Radial when option is string radial => "radial:" + radial,
            _ => null,
        };
    }

    private void Raise()
    {
        if (!_updating)
        {
            Changed?.Invoke(Build());
        }
    }

    private void OnCaptureKey(object sender, KeyEventArgs e)
    {
        if (_capture.IsChecked != true)
        {
            return;
        }

        WpfKey pressed = e.Key == WpfKey.System ? e.SystemKey : e.Key;
        if (pressed is WpfKey.LeftShift or WpfKey.RightShift or WpfKey.LeftCtrl or WpfKey.RightCtrl or WpfKey.LeftAlt or WpfKey.RightAlt)
        {
            // Only the modifier: wait for the key. (To assign Shift alone, pick it from the list.)
            e.Handled = true;
            return;
        }

        // Scancode of the physical key: it's what TyriaPad sends, whatever the keyboard layout.
        uint scancode = KeyboardInterop.MapVirtualKey((uint)KeyInterop.VirtualKeyFromKey(pressed), KeyboardInterop.MapVkToVscEx);
        if ((scancode & 0xFF00) == 0xE000 || (scancode & 0xFF00) == 0xE100)
        {
            scancode = 0xE000 | (scancode & 0xFF);
        }

        if (Enum.IsDefined(typeof(Key), (ushort)scancode))
        {
            _updating = true;
            ModifierKeys modifiers = Keyboard.Modifiers;
            _shift.IsChecked = modifiers.HasFlag(ModifierKeys.Shift);
            _ctrl.IsChecked = modifiers.HasFlag(ModifierKeys.Control);
            _alt.IsChecked = modifiers.HasFlag(ModifierKeys.Alt);
            SelectKey((Key)(ushort)scancode);
            _updating = false;
            Raise();
        }

        _capture.IsChecked = false;
        e.Handled = true;
    }
}

internal static partial class KeyboardInterop
{
    /// <summary>MAPVK_VK_TO_VSC_EX: virtual key → scancode, with the E0/E1 prefix of extended keys.</summary>
    public const uint MapVkToVscEx = 4;

    [System.Runtime.InteropServices.LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    public static partial uint MapVirtualKey(uint code, uint mapType);
}

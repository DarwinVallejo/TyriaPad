using System.Windows;
using System.Windows.Controls;

using TyriaPad.Core.Input;
using TyriaPad.Core.Keybinds;
using TyriaPad.Core.Mapping;

namespace TyriaPad.App.Settings;

/// <summary>
/// Edits what a button does in a layer: an action held while it's pressed, or gestures
/// (tap, hold, double tap). Every change is written to the draft right away; if the button
/// was inherited from another context, this one gets its own version.
/// </summary>
internal sealed class BindingEditor : StackPanel
{
    private readonly GlyphIcon _glyph = new(GamepadButtons.A, 40) { Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _where = new() { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _note = Ui.Hint(string.Empty);
    private readonly StackPanel _stick = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly RadioButton _simple = new() { Content = "Single press", GroupName = "binding-mode", Margin = new Thickness(0, 0, 16, 0) };
    private readonly RadioButton _gestures = new() { Content = "Gestures (tap / hold / double tap)", GroupName = "binding-mode" };
    private readonly StackPanel _modes = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 6) };
    private readonly StackPanel _simplePanel = new();
    private readonly StackPanel _gesturePanel = new();
    private readonly ActionPicker _press;
    private readonly ActionPicker _tap;
    private readonly ActionPicker _hold;
    private readonly ActionPicker _double;
    private readonly TextBlock _inGame = Ui.Hint(string.Empty);
    private readonly Button _remove;
    private readonly Button _revert;
    private ProfileDraft? _draft;
    private ContextDraft? _context;
    private GamepadButtons _layer;
    private GamepadButtons _button;
    private Gw2Keybinds _keybinds = Gw2Keybinds.Defaults;
    private bool _loading;

    public BindingEditor(Func<IEnumerable<string>> radials)
    {
        Margin = new Thickness(16, 0, 8, 8);
        _press = new ActionPicker(allowRadial: true, radials);
        _tap = new ActionPicker(allowRadial: false, radials);
        _hold = new ActionPicker(allowRadial: true, radials);
        _double = new ActionPicker(allowRadial: false, radials);

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(_glyph);
        var titles = new StackPanel();
        titles.Children.Add(_title);
        titles.Children.Add(_where);
        header.Children.Add(titles);
        Children.Add(header);
        _note.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(_note);
        Children.Add(_stick);

        _modes.Children.Add(_simple);
        _modes.Children.Add(_gestures);
        Children.Add(_modes);

        _simplePanel.Children.Add(Field("Action", _press, "Held while the button is pressed, with no delay."));
        Children.Add(_simplePanel);

        _gesturePanel.Children.Add(Field("Tap", _tap, "On release before the \"hold\" time (General → gestures)."));
        _gesturePanel.Children.Add(Field("Hold", _hold, "Once that time passes. A radial opens here and picks on release."));
        _gesturePanel.Children.Add(Field("Double tap", _double, "With a double tap set, the tap waits a moment in case the second one comes."));
        Children.Add(_gesturePanel);

        _inGame.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(_inGame);

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        _remove = Ui.Button("Remove from this layer", Remove);
        _revert = Ui.Button("Revert to inherited", Remove);
        actions.Children.Add(_remove);
        actions.Children.Add(_revert);
        Children.Add(actions);

        _simple.Checked += (_, _) => SwitchMode(gestures: false);
        _gestures.Checked += (_, _) => SwitchMode(gestures: true);
        foreach (ActionPicker picker in new[] { _press, _tap, _hold, _double })
        {
            picker.Changed += _ => Store();
        }
    }

    /// <summary>The draft changed (the map has to redraw that button).</summary>
    public event Action? Changed;

    public void Edit(ProfileDraft draft, ContextDraft context, GamepadButtons layer, GamepadButtons button, Gw2Keybinds keybinds)
    {
        _draft = draft;
        _context = context;
        _layer = layer;
        _button = button;
        _keybinds = keybinds;
        Load();
    }

    private static FrameworkElement Field(string label, ActionPicker picker, string hint)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        stack.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(picker);
        stack.Children.Add(Ui.Hint(hint));
        return stack;
    }

    private void Load()
    {
        if (_draft is null || _context is null)
        {
            return;
        }

        _loading = true;
        try
        {
            _glyph.Button = _button;
            _title.Text = KeyNames.ButtonName(_button);
            _where.Text = $"{KeyNames.LayerName(_layer)} layer · {ActionText.Context(_context.Name)}";

            bool isModifier = (_layer & _button) != 0;
            (BindingDraft Binding, ContextDraft From)? resolved = isModifier ? null : _draft.Resolve(_context, _layer, _button);
            BindingDraft binding = resolved?.Binding ?? new BindingDraft();
            bool own = resolved is { } r && ReferenceEquals(r.From, _context);
            bool inherited = resolved is not null && !own;

            _note.Text = isModifier
                ? $"{KeyNames.ButtonName(_button)} is this layer's modifier: it's held down to activate the layer and can't have an action in it."
                : inherited
                    ? $"Inherited from \"{ActionText.Context(resolved!.Value.From.Name)}\". If you change it, {ActionText.Context(_context.Name)} will get its own version."
                    : OpensLayerNote() ?? string.Empty;
            _note.Visibility = _note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            ShowStick();
            _modes.IsEnabled = _simplePanel.IsEnabled = _gesturePanel.IsEnabled = !isModifier;
            _simple.IsChecked = !binding.UsesGestures;
            _gestures.IsChecked = binding.UsesGestures;
            _simplePanel.Visibility = binding.UsesGestures ? Visibility.Collapsed : Visibility.Visible;
            _gesturePanel.Visibility = binding.UsesGestures ? Visibility.Visible : Visibility.Collapsed;
            _press.SetValue(binding.Press);
            _tap.SetValue(binding.Tap);
            _hold.SetValue(binding.Hold);
            _double.SetValue(binding.Double);

            UpdateButtons();
            ShowInGame(binding);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// L3/R3: what the stick does when moved. In the base layer it's the context's role; in the others,
    /// "(same as base)" keeps the base layer's and any other value changes it while the layer is held.
    /// </summary>
    private void ShowStick()
    {
        _stick.Children.Clear();
        StickSide? side = _button switch
        {
            GamepadButtons.LeftStick => StickSide.Left,
            GamepadButtons.RightStick => StickSide.Right,
            _ => null,
        };
        if (side is not { } s || _draft is not { } draft || _context is not { } context)
        {
            _stick.Visibility = Visibility.Collapsed;
            return;
        }

        bool isBase = _layer == GamepadButtons.None;
        LayerDraft? layer = context.FindLayer(_layer);
        string? own = isBase
            ? s == StickSide.Left ? context.LeftStick : context.RightStick
            : s == StickSide.Left ? layer?.LeftStick : layer?.RightStick;
        string inherited = ActionText.StickRole(isBase
            ? (draft.Parent(context) is { } parent ? draft.ResolveStick(parent, GamepadButtons.None, s) : null) ?? "none"
            : draft.ResolveStick(context, GamepadButtons.None, s) ?? "none");
        var options = new List<(string?, string)>
        {
            (null, isBase ? $"(inherited: {inherited})" : $"(same as base: {inherited})"),
            ("move", ActionText.StickRole("move")),
            ("camera", ActionText.StickRole("camera")),
            ("pointer", ActionText.StickRole("pointer")),
            ("none", ActionText.StickRole("none")),
        };
        ComboBox combo = Ui.Combo(options, own?.ToLowerInvariant(), value =>
        {
            if (isBase)
            {
                if (s == StickSide.Left)
                {
                    context.LeftStick = value;
                }
                else
                {
                    context.RightStick = value;
                }
            }
            else
            {
                LayerDraft target = context.GetOrAddLayer(_layer);
                if (s == StickSide.Left)
                {
                    target.LeftStick = value;
                }
                else
                {
                    target.RightStick = value;
                }
            }

            Changed?.Invoke();
        });
        _stick.Children.Add(new TextBlock { Text = s == StickSide.Left ? "When moving the left stick" : "When moving the right stick", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _stick.Children.Add(combo);
        _stick.Children.Add(Ui.Hint(isBase ? "Camera and cursor use their sensitivity from the Controller tab." : $"Only while {KeyNames.LayerName(_layer)} is held."));
        _stick.Children.Add(new TextBlock { Text = "When pressed (" + (s == StickSide.Left ? "L3" : "R3") + ")", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        _stick.Visibility = Visibility.Visible;
    }

    /// <summary>Note for when pressing this button opens another layer (its action here is only the quick tap).</summary>
    private string? OpensLayerNote()
    {
        if (_draft is null || _context is null)
        {
            return null;
        }

        GamepadButtons opens = _draft.OpensLayer(_context, _layer, _button);
        return opens == GamepadButtons.None
            ? null
            : $"Holding {KeyNames.ButtonName(_button)} opens layer {KeyNames.LayerName(opens)}; releasing it right away, without using the layer, does this action.";
    }

    private void ShowInGame(BindingDraft binding)
    {
        IEnumerable<string> lines = (binding.Press is not null
                ? new (string, string?)[] { ("", binding.Press) }
                : new (string, string?)[] { ("Tap: ", binding.Tap), ("Hold: ", binding.Hold), ("Double tap: ", binding.Double) })
            .Where(static p => p.Item2 is not null)
            .Select(p => (p.Item1, ActionText.InGame(p.Item2, _keybinds)))
            .Where(static p => p.Item2 is not null)
            .Select(static p => p.Item1 + p.Item2);
        string text = string.Join(Environment.NewLine, lines);
        string source = _keybinds.Source is null ? "GW2 default keys" : System.IO.Path.GetFileName(_keybinds.Source);
        _inGame.Text = text.Length == 0 ? string.Empty : $"In GW2 ({source}):{Environment.NewLine}{text}";
        _inGame.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SwitchMode(bool gestures)
    {
        if (_loading)
        {
            return;
        }

        // The main action is kept when switching mode: press ↔ tap.
        _loading = true;
        if (gestures)
        {
            _tap.SetValue(_press.Value);
        }
        else
        {
            _press.SetValue(_tap.Value ?? _hold.Value);
        }

        _loading = false;
        _simplePanel.Visibility = gestures ? Visibility.Collapsed : Visibility.Visible;
        _gesturePanel.Visibility = gestures ? Visibility.Visible : Visibility.Collapsed;
        Store();
    }

    private void Store()
    {
        if (_loading || _draft is null || _context is null || (_layer & _button) != 0)
        {
            return;
        }

        BindingDraft binding = _gestures.IsChecked == true
            ? new BindingDraft { Tap = _tap.Value, Hold = _hold.Value, Double = _double.Value }
            : new BindingDraft { Press = _press.Value };
        if (binding.IsEmpty)
        {
            _context.FindLayer(_layer)?.Bindings.Remove(_button);
        }
        else
        {
            _context.GetOrAddLayer(_layer).Bindings[_button] = binding;
        }

        ShowInGame(binding);
        UpdateButtons();
        Changed?.Invoke();
    }

    /// <summary>"Remove" if the button has its own action; "Revert to inherited" if it also inherits one.</summary>
    private void UpdateButtons()
    {
        bool own = _context?.FindLayer(_layer)?.Bindings.ContainsKey(_button) == true;
        bool inherits = own && _draft!.Parent(_context!) is { } parent && _draft.Resolve(parent, _layer, _button) is not null;
        _remove.Visibility = own && !inherits ? Visibility.Visible : Visibility.Collapsed;
        _revert.Visibility = inherits ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Remove()
    {
        if (_draft is null || _context is null)
        {
            return;
        }

        _context.FindLayer(_layer)?.Bindings.Remove(_button);
        Load();
        Changed?.Invoke();
    }
}

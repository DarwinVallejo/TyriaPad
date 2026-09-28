using System.Windows;
using System.Windows.Controls;

using TyriaPad.Core.Input;
using TyriaPad.Core.Keybinds;
using TyriaPad.Core.Mapping;

namespace TyriaPad.App.Settings;

/// <summary>
/// "Buttons" tab: picks context and layer, shows the controller with what each button does and edits
/// the tapped button. Works on a <see cref="ProfileDraft"/>; reports every change through <see cref="Changed"/>.
/// </summary>
internal sealed class ProfileEditor : DockPanel
{
    private readonly Window _owner;
    private readonly Func<Gw2Keybinds> _keybinds;
    private readonly ControllerMap _map = new() { Margin = new Thickness(8) };
    private readonly BindingEditor _editor;
    private readonly WrapPanel _contexts = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _layers = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _contextTools = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _removeLayer;
    private ProfileDraft _draft = new();
    private string _context = "combat";
    private GamepadButtons _layer;
    private bool _loading;

    public ProfileEditor(Window owner, Func<Gw2Keybinds> keybinds)
    {
        _owner = owner;
        _keybinds = keybinds;
        _editor = new BindingEditor(() => _draft.Radials.Select(static r => r.Name));
        _editor.Changed += () =>
        {
            RefreshMap();
            Changed?.Invoke();
        };
        _map.Selected += _ => EditSelected();
        _removeLayer = Ui.Button("Remove layer", RemoveLayer);

        var top = new StackPanel { Margin = new Thickness(16, 8, 16, 0) };
        top.Children.Add(Line("Context", _contexts, _contextTools));
        top.Children.Add(Line("Layer", _layers, Ui.Button("New layer…", AddLayer), _removeLayer));
        SetDock(top, Dock.Top);
        Children.Add(top);

        var side = new ScrollViewer { Content = _editor, Width = 470, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly, Margin = new Thickness(0, 8, 0, 0) };
        SetDock(side, Dock.Right);
        Children.Add(side);
        Children.Add(_map);
    }

    public event Action? Changed;

    public ProfileDraft Draft => _draft;

    public void Load(ProfileDraft draft)
    {
        _draft = draft;
        _context = "combat";
        _layer = GamepadButtons.None;
        RefreshAll();
        _map.Select(GamepadButtons.RightBumper);
    }

    /// <summary>Repaints everything (e.g. after renaming a radial in the other tab).</summary>
    public void RefreshAll()
    {
        RefreshContexts();
        RefreshLayers();
        RefreshMap();
        EditSelected();
    }

    private static FrameworkElement Line(string label, params UIElement[] content)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var text = new TextBlock { Text = label, Width = 140, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        dock.Children.Add(text);
        var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (UIElement element in content)
        {
            row.Children.Add(element);
        }

        dock.Children.Add(row);
        return dock;
    }

    private ContextDraft? CurrentContext => _draft.FindContext(_context);

    private RadioButton Choice(string group, string text, bool selected, Action chosen)
    {
        var radio = new RadioButton { Content = text, GroupName = group, IsChecked = selected, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        radio.Checked += (_, _) =>
        {
            if (!_loading)
            {
                chosen();
            }
        };
        return radio;
    }

    private void RefreshContexts()
    {
        _loading = true;
        _contexts.Children.Clear();
        foreach (string name in ProfileDraft.ContextNames)
        {
            string label = ActionText.Context(name) + (_draft.FindContext(name) is null ? " (does not exist)" : string.Empty);
            _contexts.Children.Add(Choice("profile-context", label, name == _context, () =>
            {
                _context = name;
                _layer = GamepadButtons.None;
                RefreshAll();
            }));
        }

        _contextTools.Children.Clear();
        if (CurrentContext is not { } context)
        {
            _contextTools.Children.Add(Ui.Button($"Create \"{ActionText.Context(_context)}\"", CreateContext, accent: true));
            _contextTools.Children.Add(new TextBlock
            {
                Text = _context == "mount" ? "Without it, Combat is used while mounted." : "Without it, Combat is used while the cursor is visible.",
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            _contextTools.Children.Add(new TextBlock { Text = "Inherits from", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
            var parents = new List<(string?, string)> { (null, "(none)") };
            parents.AddRange(_draft.Contexts.Where(c => c != context).Select(static c => ((string?)c.Name, ActionText.Context(c.Name))));
            ComboBox inherits = Ui.Combo(parents, context.Inherits?.ToLowerInvariant(), value =>
            {
                context.Inherits = value;
                RefreshAll();
                Changed?.Invoke();
            });
            inherits.MinWidth = 140;
            inherits.Margin = new Thickness(0, 0, 12, 0);
            _contextTools.Children.Add(inherits);
            if (_context != "combat")
            {
                _contextTools.Children.Add(Ui.Button("Remove context", RemoveContext));
            }
        }

        _loading = false;
    }

    private void RefreshLayers()
    {
        _loading = true;
        _layers.Children.Clear();
        if (CurrentContext is { } context)
        {
            foreach (GamepadButtons layer in _draft.EffectiveLayers(context))
            {
                _layers.Children.Add(Choice("profile-layer", KeyNames.LayerName(layer), layer == _layer, () =>
                {
                    _layer = layer;
                    RefreshMap();
                    RefreshLayerTools();
                    EditSelected();
                }));
            }
        }

        _loading = false;
        RefreshLayerTools();
    }

    private void RefreshLayerTools()
        => _removeLayer.IsEnabled = _layer != GamepadButtons.None && CurrentContext?.FindLayer(_layer) is not null;

    private void RefreshMap()
    {
        ContextDraft? context = CurrentContext;
        Gw2Keybinds keybinds = _keybinds();
        foreach (GamepadButtons button in ProfileDraft.Buttons)
        {
            if (context is null)
            {
                _map.Update(button, new TileInfo(TileState.Empty, "—", null));
                continue;
            }

            // The sticks also say what they do when moved in this layer.
            string? stick = button switch
            {
                GamepadButtons.LeftStick => "Stick: " + ActionText.StickRole(_draft.ResolveStick(context, _layer, StickSide.Left) ?? "none"),
                GamepadButtons.RightStick => "Stick: " + ActionText.StickRole(_draft.ResolveStick(context, _layer, StickSide.Right) ?? "none"),
                _ => null,
            };

            if ((_layer & button) != 0)
            {
                _map.Update(button, new TileInfo(TileState.Modifier, "(held)", stick ?? "layer modifier"));
                continue;
            }

            GamepadButtons opens = _draft.OpensLayer(context, _layer, button);
            string? opensText = opens == GamepadButtons.None ? null : $"opens layer {KeyNames.LayerName(opens)}";
            if (_draft.Resolve(context, _layer, button) is not { } resolved)
            {
                _map.Update(button, new TileInfo(TileState.Empty, "—", stick ?? opensText));
                continue;
            }

            // The main action on top; below, hold/double tap or, if there are none, what it does in GW2.
            BindingDraft binding = resolved.Binding;
            bool own = ReferenceEquals(resolved.From, context);
            string main = ActionText.Describe(binding.Press ?? binding.Tap, compact: true);
            string? gestures = binding.UsesGestures && (binding.Hold ?? binding.Double) is not null
                ? string.Join(" · ", new[] { binding.Hold is null ? null : "hold " + ActionText.Describe(binding.Hold, compact: true), binding.Double is null ? null : "×2 " + ActionText.Describe(binding.Double, compact: true) }.OfType<string>())
                : null;
            string? detail = stick ?? gestures ?? opensText ?? (_context == "pointer" ? null : ActionText.InGame(binding, keybinds));
            if (!own)
            {
                detail = $"from {ActionText.Context(resolved.From.Name)}" + (detail is null ? string.Empty : $" · {detail}");
            }

            _map.Update(button, new TileInfo(own ? TileState.Own : TileState.Inherited, main, detail));
        }
    }

    /// <summary>Shows a specific context, layer and button (for the diagnostic snapshots).</summary>
    internal void Show(string context, GamepadButtons layer, GamepadButtons button)
    {
        _context = context;
        _layer = layer;
        RefreshAll();
        _map.Select(button);
    }

    private void EditSelected()
    {
        if (CurrentContext is { } context)
        {
            _editor.Visibility = Visibility.Visible;
            _editor.Edit(_draft, context, _layer, _map.SelectedButton, _keybinds());
        }
        else
        {
            _editor.Visibility = Visibility.Collapsed;
        }
    }

    private void CreateContext()
    {
        var context = new ContextDraft(_context);
        if (_context == "mount")
        {
            context.Inherits = "combat";
        }
        else if (_context == "pointer")
        {
            context.LeftStick = "pointer";
            context.RightStick = "pointer";
        }

        context.GetOrAddLayer(GamepadButtons.None);
        _draft.Contexts.Add(context);
        RefreshAll();
        Changed?.Invoke();
    }

    private void RemoveContext()
    {
        if (CurrentContext is not { } context || Dialogs.Ask(_owner, "Remove context", $"Remove the {ActionText.Context(_context)} context with all its buttons?") != MessageBoxResult.Yes)
        {
            return;
        }

        _draft.Contexts.Remove(context);
        foreach (ContextDraft child in _draft.Contexts.Where(c => string.Equals(c.Inherits, context.Name, StringComparison.OrdinalIgnoreCase)))
        {
            child.Inherits = null;
        }

        _context = "combat";
        _layer = GamepadButtons.None;
        RefreshAll();
        Changed?.Invoke();
    }

    private void AddLayer()
    {
        if (CurrentContext is not { } context || Dialogs.ChooseModifiers(_owner, _draft.EffectiveLayers(context).ToList()) is not { } modifiers)
        {
            return;
        }

        context.GetOrAddLayer(modifiers);
        _layer = modifiers;
        RefreshAll();
        Changed?.Invoke();
    }

    private void RemoveLayer()
    {
        if (CurrentContext?.FindLayer(_layer) is not { } layer)
        {
            return;
        }

        if (layer.Bindings.Count > 0 && Dialogs.Ask(_owner, "Remove layer", $"Remove layer {layer.Name} from {ActionText.Context(_context)} with its {layer.Bindings.Count} buttons?") != MessageBoxResult.Yes)
        {
            return;
        }

        CurrentContext.Layers.Remove(layer);
        if (!_draft.EffectiveLayers(CurrentContext).Contains(_layer))
        {
            _layer = GamepadButtons.None;
        }

        RefreshAll();
        Changed?.Invoke();
    }
}

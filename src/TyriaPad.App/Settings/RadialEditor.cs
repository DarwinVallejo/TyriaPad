using System.Windows;
using System.Windows.Controls;

using TyriaPad.Core.Mapping;

namespace TyriaPad.App.Settings;

/// <summary>
/// "Radials" tab: the menus that open by holding a button. Each option is a label and
/// a single action; they are spread in a circle starting at the top and going clockwise.
/// </summary>
internal sealed class RadialEditor : DockPanel
{
    private readonly Window _owner;
    private readonly ListBox _list = new() { Width = 220, Margin = new Thickness(0, 0, 16, 0) };
    private readonly StackPanel _detail = new();
    private ProfileDraft _draft = new();

    public RadialEditor(Window owner)
    {
        _owner = owner;
        Margin = new Thickness(24, 12, 24, 12);

        var left = new DockPanel();
        var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        tools.Children.Add(Ui.Button("New…", Add));
        tools.Children.Add(Ui.Button("Rename…", Rename));
        tools.Children.Add(Ui.Button("Delete", Delete));
        tools.Width = 236;
        SetDock(tools, Dock.Bottom);
        left.Children.Add(tools);
        left.Children.Add(_list);
        SetDock(left, Dock.Left);
        Children.Add(left);

        Children.Add(new ScrollViewer { Content = _detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly });
        _list.SelectionChanged += (_, _) => ShowSelected();
    }

    /// <summary>The draft changed.</summary>
    public event Action? Changed;

    /// <summary>The name or the list of radials changed (the button editor has to refresh).</summary>
    public event Action? RadialsChanged;

    public void Load(ProfileDraft draft)
    {
        _draft = draft;
        RefreshList(draft.Radials.FirstOrDefault());
    }

    private RadialDraft? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as RadialDraft;

    private void RefreshList(RadialDraft? select)
    {
        _list.Items.Clear();
        foreach (RadialDraft radial in _draft.Radials)
        {
            var item = new ListBoxItem { Content = $"{radial.Name}  ({radial.Items.Count})", Tag = radial, Padding = new Thickness(10, 8, 10, 8) };
            _list.Items.Add(item);
            if (radial == select)
            {
                _list.SelectedItem = item;
            }
        }

        ShowSelected();
    }

    private void ShowSelected()
    {
        _detail.Children.Clear();
        if (Selected is not { } radial)
        {
            _detail.Children.Add(Ui.Hint("Create a radial with \"New…\" and open it from the Buttons tab with the \"Radial\" action (in \"Hold\" or in a single press)."));
            return;
        }

        _detail.Children.Add(new TextBlock { Text = radial.Name, FontSize = 20, FontWeight = FontWeights.SemiBold });
        _detail.Children.Add(Ui.Hint(_draft.IsRadialUsed(radial.Name)
            ? "Holding the button opens the menu; the stick picks and releasing sends the pointed option (a single action)."
            : "No button opens this radial yet: assign it in the Buttons tab."));
        _detail.Children.Add(Ui.Row("Picked with the stick", Ui.Combo([("left", "Left"), ("right", "Right")], radial.Stick.ToLowerInvariant(), value =>
        {
            radial.Stick = value;
            Changed?.Invoke();
        })));

        _detail.Children.Add(Ui.Heading("Options (1 at the top, clockwise)"));
        for (int i = 0; i < radial.Items.Count; i++)
        {
            _detail.Children.Add(ItemRow(radial, i));
        }

        var add = Ui.Button("Add option", () =>
        {
            radial.Items.Add(new RadialItemDraft("New", "F"));
            Edited(radial);
        });
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 10, 0, 0);
        _detail.Children.Add(add);
        if (radial.Items.Count < 2)
        {
            _detail.Children.Add(Ui.Hint("A radial needs at least two options."));
        }
    }

    private FrameworkElement ItemRow(RadialDraft radial, int index)
    {
        RadialItemDraft item = radial.Items[index];
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(new TextBlock { Text = $"{index + 1}.", Width = 28, VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBox { Text = item.Label, Width = 200, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        label.TextChanged += (_, _) =>
        {
            radial.Items[index] = radial.Items[index] with { Label = label.Text };
            Changed?.Invoke();
        };
        row.Children.Add(label);

        var action = new ActionPicker(allowRadial: false, () => []) { Margin = new Thickness(0, 0, 8, 0) };
        action.SetValue(item.Action);
        action.Changed += value =>
        {
            radial.Items[index] = radial.Items[index] with { Action = value ?? string.Empty };
            Changed?.Invoke();
        };
        row.Children.Add(action);

        row.Children.Add(Small("↑", index > 0, () => Move(radial, index, index - 1)));
        row.Children.Add(Small("↓", index < radial.Items.Count - 1, () => Move(radial, index, index + 1)));
        row.Children.Add(Small("✕", true, () =>
        {
            radial.Items.RemoveAt(index);
            Edited(radial);
        }));
        return row;
    }

    private static Button Small(string text, bool enabled, Action click)
    {
        Button button = Ui.Button(text, click);
        button.MinWidth = 40;
        button.Padding = new Thickness(8, 4, 8, 4);
        button.Margin = new Thickness(0, 0, 4, 0);
        button.IsEnabled = enabled;
        return button;
    }

    private void Move(RadialDraft radial, int from, int to)
    {
        (radial.Items[from], radial.Items[to]) = (radial.Items[to], radial.Items[from]);
        Edited(radial);
    }

    private void Edited(RadialDraft radial)
    {
        RefreshList(radial);
        Changed?.Invoke();
    }

    private string? ValidateName(string name, RadialDraft? except)
        => name.Length == 0 ? "Type a name."
        : name.Any(static c => char.IsWhiteSpace(c) || c is ':' or '"') ? "No spaces, quotes or ':' (it's written as radial:<name>)."
        : _draft.FindRadial(name) is { } other && other != except ? "There is already a radial with that name."
        : null;

    private void Add()
    {
        if (Dialogs.Prompt(_owner, "New radial", "Radial name (e.g. \"weapons\"):", "new", n => ValidateName(n, null)) is not { } name)
        {
            return;
        }

        var radial = new RadialDraft(name);
        radial.Items.Add(new RadialItemDraft("Option 1", "F1"));
        radial.Items.Add(new RadialItemDraft("Option 2", "F2"));
        _draft.Radials.Add(radial);
        RefreshList(radial);
        Changed?.Invoke();
        RadialsChanged?.Invoke();
    }

    private void Rename()
    {
        if (Selected is not { } radial
            || Dialogs.Prompt(_owner, "Rename radial", "New name. The buttons that open it update on their own.", radial.Name, n => ValidateName(n, radial)) is not { } name)
        {
            return;
        }

        _draft.RenameRadial(radial, name);
        RefreshList(radial);
        Changed?.Invoke();
        RadialsChanged?.Invoke();
    }

    private void Delete()
    {
        if (Selected is not { } radial)
        {
            return;
        }

        string message = _draft.IsRadialUsed(radial.Name)
            ? $"Some buttons open \"{radial.Name}\". If you delete it, they will have to be changed before saving. Delete it?"
            : $"Delete the radial \"{radial.Name}\"?";
        if (Dialogs.Ask(_owner, "Delete radial", message) != MessageBoxResult.Yes)
        {
            return;
        }

        _draft.Radials.Remove(radial);
        RefreshList(_draft.Radials.FirstOrDefault());
        Changed?.Invoke();
        RadialsChanged?.Invoke();
    }
}

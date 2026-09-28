using System.Windows;
using System.Windows.Controls;

using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;

namespace TyriaPad.App.Settings;

/// <summary>Small dialogs of the settings window, with the same theme.</summary>
internal static class Dialogs
{
    /// <summary>Asks for a text. null if cancelled.</summary>
    public static string? Prompt(Window owner, string title, string message, string initial, Func<string, string?> validate)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 10, 0, 4), MinWidth = 320 };
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        content.Children.Add(box);
        content.Children.Add(error);

        string? result = null;
        Window dialog = Build(owner, title, content, () =>
        {
            string text = box.Text.Trim();
            if (validate(text) is { } problem)
            {
                error.Text = problem;
                error.Visibility = Visibility.Visible;
                return false;
            }

            result = text;
            return true;
        });
        dialog.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return dialog.ShowDialog() == true ? result : null;
    }

    /// <summary>Picks the modifiers of a new layer. null if cancelled.</summary>
    public static GamepadButtons? ChooseModifiers(Window owner, IReadOnlyCollection<GamepadButtons> existing)
    {
        var checks = new List<(GamepadButtons Button, CheckBox Box)>();
        var grid = new WrapPanel { MaxWidth = 460, Margin = new Thickness(0, 10, 0, 0) };
        foreach (GamepadButtons button in ProfileDraft.Buttons)
        {
            var box = new CheckBox { Margin = new Thickness(0, 4, 18, 4), MinWidth = 90 };
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new GlyphIcon(button, 24) { Margin = new Thickness(0, 0, 6, 0) });
            label.Children.Add(new TextBlock { Text = KeyNames.ButtonName(button), VerticalAlignment = VerticalAlignment.Center });
            box.Content = label;
            grid.Children.Add(box);
            checks.Add((button, box));
        }

        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "Buttons to hold to activate the layer. Usually one or two (LB, LT, LT+RT). A button that opens a layer still does its base layer action with a quick tap.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
        });
        content.Children.Add(grid);
        content.Children.Add(error);

        GamepadButtons chosen = GamepadButtons.None;
        Window dialog = Build(owner, "New layer", content, () =>
        {
            chosen = checks.Where(static c => c.Box.IsChecked == true).Aggregate(GamepadButtons.None, static (all, c) => all | c.Button);
            string? problem = chosen == GamepadButtons.None ? "Pick at least one button."
                : existing.Contains(chosen) ? $"Layer {KeyNames.LayerName(chosen)} already exists."
                : null;
            error.Text = problem ?? string.Empty;
            error.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
            return problem is null;
        });
        return dialog.ShowDialog() == true ? chosen : null;
    }

    public static MessageBoxResult Ask(Window owner, string title, string message, MessageBoxButton buttons = MessageBoxButton.YesNo)
        => MessageBox.Show(owner, message, title, buttons, MessageBoxImage.Question);

    private static Window Build(Window owner, string title, UIElement content, Func<bool> accept)
    {
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 100, Padding = new Thickness(12, 6, 12, 6) };
        ok.Loaded += (_, _) => ok.Style = ok.TryFindResource("AccentButtonStyle") as Style ?? ok.Style;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(content);
        root.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title,
            Owner = owner,
            Content = root,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        SettingsWindow.ApplyTheme(dialog);
        ok.Click += (_, _) =>
        {
            if (accept())
            {
                dialog.DialogResult = true;
            }
        };
        return dialog;
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using TyriaPad.App.Overlay;
using TyriaPad.Core.Input;

namespace TyriaPad.App.Settings;

/// <summary>Vector glyph of a controller button (the same one the overlay draws).</summary>
internal sealed class GlyphIcon : FrameworkElement
{
    private GamepadButtons _button;

    public GlyphIcon(GamepadButtons button, double size)
    {
        _button = button;
        Height = size;
        Width = GlyphPainter.WidthOf(button, size) + 2;
        IsHitTestVisible = false;
    }

    public GamepadButtons Button
    {
        get => _button;
        set
        {
            _button = value;
            Width = GlyphPainter.WidthOf(value, Height) + 2;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double size = ActualHeight;
        GlyphPainter.Draw(drawingContext, _button, new Point(ActualWidth / 2, size / 2), size * 0.92, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}

/// <summary>Form pieces of the settings window, also meant to be tapped on the Ally's screen.</summary>
internal static class Ui
{
    public const double LabelWidth = 250;

    public static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 18, 0, 6),
    };

    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.7,
        FontSize = 12.5,
        Margin = new Thickness(0, 0, 0, 4),
    };

    /// <summary>"label | control" row with an optional hint below.</summary>
    public static FrameworkElement Row(string label, UIElement control, string? hint = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
        grid.Children.Add(text);
        Grid.SetColumn((UIElement)control, 1);
        grid.Children.Add(control);
        if (hint is null)
        {
            return grid;
        }

        var stack = new StackPanel();
        stack.Children.Add(grid);
        TextBlock hintText = Hint(hint);
        hintText.Margin = new Thickness(LabelWidth, 0, 0, 6);
        stack.Children.Add(hintText);
        return stack;
    }

    /// <summary>Slider with the value written on the right.</summary>
    public static FrameworkElement Slider(string label, double min, double max, double step, string format, double value, Action<double> changed, string? hint = null)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            SmallChange = step,
            LargeChange = step * 10,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 220,
        };
        var text = new TextBlock
        {
            Text = value.ToString(format, CultureInfo.CurrentCulture),
            Width = 80,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, e) =>
        {
            double rounded = Math.Round(e.NewValue / step) * step;
            text.Text = rounded.ToString(format, CultureInfo.CurrentCulture);
            changed(rounded);
        };
        var dock = new DockPanel();
        DockPanel.SetDock(text, Dock.Right);
        dock.Children.Add(text);
        dock.Children.Add(slider);
        return Row(label, dock, hint);
    }

    public static FrameworkElement Check(string label, bool value, Action<bool> changed, string? hint = null)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 6, 0, 2) };
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        if (hint is null)
        {
            return box;
        }

        var stack = new StackPanel();
        stack.Children.Add(box);
        TextBlock hintText = Hint(hint);
        hintText.Margin = new Thickness(28, 0, 0, 6);
        stack.Children.Add(hintText);
        return stack;
    }

    /// <summary>Drop-down list of options (value, text).</summary>
    public static ComboBox Combo<T>(IEnumerable<(T Value, string Text)> options, T selected, Action<T> changed)
    {
        var combo = new ComboBox { MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
        foreach ((T value, string text) in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = text, Tag = value });
            if (EqualityComparer<T>.Default.Equals(value, selected))
            {
                combo.SelectedIndex = combo.Items.Count - 1;
            }
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: T value })
            {
                changed(value);
            }
        };
        return combo;
    }

    public static Button Button(string text, Action click, bool accent = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), MinHeight = 34 };
        if (accent)
        {
            // The accent style comes from the window's theme: it's looked up once the button is in the window.
            button.Loaded += (_, _) => button.Style = button.TryFindResource("AccentButtonStyle") as Style ?? button.Style;
        }

        button.Click += (_, _) => click();
        return button;
    }

    public static ScrollViewer Page(params UIElement[] children)
    {
        var stack = new StackPanel { Margin = new Thickness(24, 8, 24, 24), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (UIElement child in children)
        {
            stack.Children.Add(child);
        }

        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly };
    }
}

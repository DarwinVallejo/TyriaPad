using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using TyriaPad.Core.Input;

namespace TyriaPad.App.Settings;

/// <summary>How a button looks on the map.</summary>
internal enum TileState
{
    /// <summary>Has its own action in this layer.</summary>
    Own,

    /// <summary>The action comes from another context (inheritance).</summary>
    Inherited,

    /// <summary>No action.</summary>
    Empty,

    /// <summary>It's the layer's modifier: held to activate the layer, and can't have an action.</summary>
    Modifier,
}

internal sealed record TileInfo(TileState State, string Action, string? Detail);

/// <summary>
/// The controller buttons laid out as on an Xbox controller (triggers on top, D-pad bottom
/// left, face buttons on the right, M1/M2 at the bottom). It scales as a whole with the available space.
/// Tapping a button selects it (<see cref="Selected"/>).
/// </summary>
internal sealed class ControllerMap : Viewbox
{
    private const double TileWidth = 176;
    private const double TileHeight = 62;

    // Position (top-left corner) of each button on an 800x540 canvas.
    private static readonly (GamepadButtons Button, double X, double Y)[] s_layout =
    [
        (GamepadButtons.LeftTrigger, 16, 8), (GamepadButtons.RightTrigger, 608, 8),
        (GamepadButtons.LeftBumper, 16, 80), (GamepadButtons.View, 214, 80), (GamepadButtons.Menu, 410, 80), (GamepadButtons.RightBumper, 608, 80),
        (GamepadButtons.LeftStick, 16, 166), (GamepadButtons.Y, 512, 160),
        (GamepadButtons.X, 416, 228), (GamepadButtons.B, 608, 228),
        (GamepadButtons.DPadUp, 150, 262), (GamepadButtons.A, 512, 296),
        (GamepadButtons.DPadLeft, 16, 330), (GamepadButtons.DPadRight, 284, 330), (GamepadButtons.RightStick, 480, 378),
        (GamepadButtons.DPadDown, 150, 398),
        (GamepadButtons.M1, 16, 470), (GamepadButtons.M2, 608, 470),
    ];

    private readonly Dictionary<GamepadButtons, Tile> _tiles = [];
    private GamepadButtons _selected;

    public ControllerMap()
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.Both;
        var canvas = new Canvas { Width = 800, Height = 540 };

        // Faint controller silhouette behind the buttons, to help find your way.
        canvas.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Width = 560,
            Height = 330,
            RadiusX = 150,
            RadiusY = 150,
            Opacity = 0.08,
            Fill = Brushes.Gray,
        });
        Canvas.SetLeft(canvas.Children[0], 120);
        Canvas.SetTop(canvas.Children[0], 120);

        foreach ((GamepadButtons button, double x, double y) in s_layout)
        {
            var tile = new Tile(button);
            tile.MouseLeftButtonUp += (_, _) => Select(button);
            tile.TouchUp += (_, e) =>
            {
                Select(button);
                e.Handled = true;
            };
            Canvas.SetLeft(tile, x);
            Canvas.SetTop(tile, y);
            canvas.Children.Add(tile);
            _tiles[button] = tile;
        }

        Child = canvas;
    }

    public event Action<GamepadButtons>? Selected;

    public GamepadButtons SelectedButton => _selected;

    public void Select(GamepadButtons button)
    {
        _selected = button;
        foreach ((GamepadButtons b, Tile tile) in _tiles)
        {
            tile.IsSelected = b == button;
        }

        Selected?.Invoke(button);
    }

    public void Update(GamepadButtons button, TileInfo info)
    {
        if (_tiles.TryGetValue(button, out Tile? tile))
        {
            tile.Show(info);
        }
    }

    private sealed class Tile : Border
    {
        private readonly TextBlock _action = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _detail = new() { FontSize = 11.5, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
        private bool _selected;
        private TileState _state;

        public Tile(GamepadButtons button)
        {
            Width = TileWidth;
            Height = TileHeight;
            CornerRadius = new CornerRadius(10);
            BorderThickness = new Thickness(2);
            Padding = new Thickness(8, 4, 8, 4);
            Cursor = Cursors.Hand;
            SetResourceReference(BackgroundProperty, SystemColors.ControlBrushKey);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var glyph = new GlyphIcon(button, 34) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(glyph);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(_action);
            text.Children.Add(_detail);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            Child = grid;
            Refresh();
        }

        public bool IsSelected
        {
            get => _selected;
            set
            {
                _selected = value;
                Refresh();
            }
        }

        public void Show(TileInfo info)
        {
            _state = info.State;
            _action.Text = info.Action;
            _detail.Text = info.Detail ?? string.Empty;
            _detail.Visibility = info.Detail is null ? Visibility.Collapsed : Visibility.Visible;
            ToolTip = info.Detail is null ? info.Action : $"{info.Action}\n{info.Detail}";
            Refresh();
        }

        private void Refresh()
        {
            Brush accent = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? SystemColors.HighlightBrush;
            BorderBrush = _selected ? accent : _state == TileState.Modifier ? accent : new SolidColorBrush(Color.FromArgb(60, 128, 128, 128));
            BorderThickness = new Thickness(_selected ? 3 : _state == TileState.Modifier ? 1.5 : 1);
            Opacity = _state switch
            {
                TileState.Empty => 0.55,
                TileState.Modifier => 0.8,
                _ => 1,
            };
            _action.FontStyle = _state == TileState.Inherited ? FontStyles.Italic : FontStyles.Normal;
            _action.FontWeight = _state == TileState.Own ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
}

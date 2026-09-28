using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using TyriaPad.Core.Overlay;

namespace TyriaPad.App.Overlay;

/// <summary>
/// Calibration mode: five draggable markers (slots 1, 5 and 10 of the skill bar and
/// F1, F2 of the profession bar) and a panel with Save / Default / Cancel. Works in
/// WPF units; converts to physical pixels with the DPI scale it receives in <see cref="Begin"/>.
/// </summary>
internal sealed class CalibrationLayer : Canvas
{
    private const double MarkerSize = 44;

    private readonly Marker[] _markers;
    private readonly Border _panel;
    private readonly TextBlock _title;
    private double _dpiScale = 1;
    private SkillBarCalibration _defaults = new(default, default, default, default, default);

    public CalibrationLayer()
    {
        Background = Brushes.Transparent; // receives clicks over the whole area
        _markers =
        [
            new Marker("1", Color.FromRgb(0xF7, 0xC5, 0x31)),
            new Marker("5", Color.FromRgb(0xF7, 0xC5, 0x31)),
            new Marker("10", Color.FromRgb(0xF7, 0xC5, 0x31)),
            new Marker("F1", Color.FromRgb(0x4C, 0x8D, 0xFF)),
            new Marker("F2", Color.FromRgb(0x4C, 0x8D, 0xFF)),
        ];
        foreach (Marker marker in _markers)
        {
            marker.Moved += () => Changed?.Invoke();
            Children.Add(marker);
        }

        _title = new TextBlock { Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, MaxWidth = 640 };
        var help = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 640,
            Margin = new Thickness(0, 6, 0, 10),
            Text = "Drag each marker to the center of its slot: 1, 5 and 10 on the skill bar and F1, F2 on the profession bar. "
                + "The boxes show where the rest will go. The controller keeps working in the game meanwhile.",
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(MakeButton("Save", () => Saved?.Invoke(Read())));
        buttons.Children.Add(MakeButton("Default", ResetToDefaults));
        buttons.Children.Add(MakeButton("Cancel", () => Cancelled?.Invoke()));
        var stack = new StackPanel();
        stack.Children.Add(_title);
        stack.Children.Add(help);
        stack.Children.Add(buttons);
        _panel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 18, 18, 22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 14, 18, 14),
            Child = stack,
        };
        Children.Add(_panel);
        SizeChanged += (_, _) => PlacePanel();
    }

    /// <summary>A marker moved: the boxes must be drawn again.</summary>
    public event Action? Changed;

    public event Action<SkillBarCalibration>? Saved;

    public event Action? Cancelled;

    public void Begin(SkillBarCalibration current, SkillBarCalibration defaults, double dpiScale, string key)
    {
        _dpiScale = dpiScale;
        _defaults = defaults;
        _title.Text = $"Overlay calibration ({key})";
        Apply(current);
        Visibility = Visibility.Visible;
        PlacePanel();
    }

    public void End() => Visibility = Visibility.Collapsed;

    /// <summary>Current marker positions, in physical pixels of the client area.</summary>
    public SkillBarCalibration Read()
        => new(Position(0), Position(1), Position(2), Position(3), Position(4));

    private void ResetToDefaults()
    {
        Apply(_defaults);
        Changed?.Invoke();
    }

    private void Apply(SkillBarCalibration calibration)
    {
        Point2[] points = [calibration.Slot1, calibration.Slot5, calibration.Slot10, calibration.F1, calibration.F2];
        for (int i = 0; i < _markers.Length; i++)
        {
            _markers[i].SetCenter(new Point(points[i].X / _dpiScale, points[i].Y / _dpiScale));
        }
    }

    private Point2 Position(int index)
    {
        Point center = _markers[index].Center;
        return new Point2((float)(center.X * _dpiScale), (float)(center.Y * _dpiScale));
    }

    private void PlacePanel()
    {
        _panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(_panel, Math.Max(0, (ActualWidth - _panel.DesiredSize.Width) / 2));
        SetTop(_panel, Math.Max(0, ActualHeight * 0.12));
    }

    private static Button MakeButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(6, 0, 6, 0),
            Padding = new Thickness(18, 8, 18, 8),
            FontSize = 14,
            MinWidth = 110,
            Focusable = false,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Labeled circle dragged with the mouse (or a finger on the touch screen).</summary>
    private sealed class Marker : Grid
    {
        private Point _grabOffset;

        public Marker(string label, Color color)
        {
            Width = MarkerSize;
            Height = MarkerSize;
            Cursor = Cursors.SizeAll;
            Children.Add(new Ellipse
            {
                Fill = new SolidColorBrush(Color.FromArgb(215, 18, 18, 22)),
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 3,
            });
            Children.Add(new Ellipse { Width = 4, Height = 4, Fill = new SolidColorBrush(color) });
            Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(color),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        public event Action? Moved;

        public Point Center => new(GetLeft(this) + (MarkerSize / 2), GetTop(this) + (MarkerSize / 2));

        public void SetCenter(Point center)
        {
            SetLeft(this, center.X - (MarkerSize / 2));
            SetTop(this, center.Y - (MarkerSize / 2));
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            _grabOffset = e.GetPosition(this);
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!IsMouseCaptured || Parent is not Canvas canvas)
            {
                return;
            }

            Point p = e.GetPosition(canvas);
            SetLeft(this, p.X - _grabOffset.X);
            SetTop(this, p.Y - _grabOffset.Y);
            Moved?.Invoke();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
                e.Handled = true;
            }
        }
    }
}

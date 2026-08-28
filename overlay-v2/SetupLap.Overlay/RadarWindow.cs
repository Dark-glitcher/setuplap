using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SetupLapOverlay;

public sealed class RadarWindow : WidgetWindow
{
    readonly Canvas _canvas = new();
    OverlaySnapshot? _snapshot;

    public RadarWindow() : base("Radar", 260, 230, (SystemParameters.PrimaryScreenWidth - 260) / 2, SystemParameters.PrimaryScreenHeight - 500)
    {
        Body.Children.Add(_canvas);
        _canvas.SizeChanged += (s,e) => Draw();

        // Natural HUD treatment: no panel outline, no header bar, no box background.
        if (Content is Border frame && frame.Child is Grid root)
        {
            frame.Background = Brushes.Transparent;
            frame.BorderThickness = new Thickness(0);
            frame.CornerRadius = new CornerRadius(0);
            if (root.RowDefinitions.Count >= 2)
            {
                root.RowDefinitions[0].Height = new GridLength(0);
                Grid.SetRow(Body, 0);
                Grid.SetRowSpan(Body, 2);
            }
        }
    }

    public override void Update(OverlaySnapshot s)
    {
        _snapshot = s;
        Draw();
    }

    void Draw()
    {
        if (_snapshot is null) return;
        double w = _canvas.ActualWidth, h = _canvas.ActualHeight;
        if (w < 20 || h < 20) return;
        _canvas.Children.Clear();

        double cx = w / 2, cy = h / 2;

        // Player car: only the useful radar geometry remains visible.
        var car = new Border
        {
            Width = 30, Height = 58,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(215,255,77,23)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150,255,255,255)),
            BorderThickness = new Thickness(1)
        };
        Canvas.SetLeft(car, cx - 15); Canvas.SetTop(car, cy - 29); _canvas.Children.Add(car);

        if (!_snapshot.Connected) return;

        // iRacing native spotter state:
        // 0 Off, 1 Clear, 2 Left, 3 Right, 4 Both, 5 Two Left, 6 Two Right.
        bool left = _snapshot.CarLeftRight is 2 or 4 or 5;
        bool right = _snapshot.CarLeftRight is 3 or 4 or 6;
        int leftCount = _snapshot.CarLeftRight == 5 ? 2 : left ? 1 : 0;
        int rightCount = _snapshot.CarLeftRight == 6 ? 2 : right ? 1 : 0;

        if (left) DrawSide(true, leftCount, cx, cy);
        if (right) DrawSide(false, rightCount, cx, cy);

        // Front/rear traffic comes from relative timing; left/right is always the native spotter state.
        var close = _snapshot.Relatives
            .Where(r => !r.IsPlayer && Math.Abs(r.GapSeconds) <= 1.15)
            .OrderBy(r => Math.Abs(r.GapSeconds))
            .Take(4)
            .ToList();

        int ahead = 0, behind = 0;
        foreach (var r in close)
        {
            bool isAhead = r.GapSeconds > 0;
            int slot = isAhead ? ahead++ : behind++;
            if (slot >= 2) continue;

            double y = isAhead ? cy - 72 - slot * 23 : cy + 52 + slot * 23;
            var badge = new Border
            {
                Width = 54, Height = 18,
                CornerRadius = new CornerRadius(4),
                Background = r.ClassBrush,
                Opacity = .92,
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(r.Number) ? "CAR" : r.Number,
                    Foreground = Brushes.White,
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(badge, cx - 27); Canvas.SetTop(badge, y); _canvas.Children.Add(badge);
        }

        if (_snapshot.CarLeftRight is 4 or 5 or 6)
            AddStatus("THREE WIDE", Theme.Red, w, h);
        else if (left)
            AddStatus("CAR LEFT", Theme.Orange, w, h);
        else if (right)
            AddStatus("CAR RIGHT", Theme.Orange, w, h);
        else if (close.Any(r => Math.Abs(r.GapSeconds) < .35))
            AddStatus("CLOSE", Theme.Red, w, h);
    }

    void DrawSide(bool left, int count, double cx, double cy)
    {
        double x = left ? cx - 66 : cx + 38;
        for (int i = 0; i < count; i++)
        {
            var marker = new Border
            {
                Width = 28, Height = 54,
                CornerRadius = new CornerRadius(5),
                Background = Theme.Red,
                Opacity = .92
            };
            Canvas.SetLeft(marker, x + (left ? -i * 16 : i * 16));
            Canvas.SetTop(marker, cy - 27 + i * 4);
            _canvas.Children.Add(marker);
        }
    }

    void AddStatus(string text, Brush brush, double w, double h)
    {
        var t = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Width = w
        };
        Canvas.SetLeft(t, 0); Canvas.SetTop(t, h - 22); _canvas.Children.Add(t);
    }
}

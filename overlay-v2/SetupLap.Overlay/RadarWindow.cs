using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SetupLapOverlay;

public sealed class RadarWindow : WidgetWindow
{
    readonly Canvas _canvas = new();
    OverlaySnapshot? _snapshot;

    public RadarWindow() : base("Radar", 260, 230, (SystemParameters.PrimaryScreenWidth - 260) / 2, SystemParameters.PrimaryScreenHeight - 500)
    {
        Body.Children.Add(_canvas);
        _canvas.SizeChanged += (s,e) => Draw();
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

        // Radar field.
        var guide = new Rectangle
        {
            Width = 164, Height = 164,
            Stroke = new SolidColorBrush(Color.FromArgb(42,255,255,255)),
            StrokeThickness = 1,
            RadiusX = 14, RadiusY = 14
        };
        Canvas.SetLeft(guide, cx - 82); Canvas.SetTop(guide, cy - 82); _canvas.Children.Add(guide);

        var centreLine = new Line
        {
            X1 = cx, X2 = cx, Y1 = cy - 80, Y2 = cy + 80,
            Stroke = new SolidColorBrush(Color.FromArgb(28,255,255,255)), StrokeThickness = 1
        };
        _canvas.Children.Add(centreLine);

        // Player car.
        var car = new Border
        {
            Width = 34, Height = 64,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(220, 255, 77, 23)),
            BorderBrush = Theme.Text, BorderThickness = new Thickness(1)
        };
        Canvas.SetLeft(car, cx - 17); Canvas.SetTop(car, cy - 32); _canvas.Children.Add(car);

        if (!_snapshot.Connected)
        {
            AddStatus("WAITING FOR IRACING", Theme.Muted, w, h);
            return;
        }

        // CarLeftRight is iRacing's native spotter state:
        // 0 Off, 1 Clear, 2 Left, 3 Right, 4 Both, 5 Two Left, 6 Two Right.
        bool left = _snapshot.CarLeftRight is 2 or 4 or 5;
        bool right = _snapshot.CarLeftRight is 3 or 4 or 6;
        int leftCount = _snapshot.CarLeftRight == 5 ? 2 : left ? 1 : 0;
        int rightCount = _snapshot.CarLeftRight == 6 ? 2 : right ? 1 : 0;

        if (left) DrawSide(true, leftCount, cx, cy);
        if (right) DrawSide(false, rightCount, cx, cy);

        // iRacing does not expose exact world X/Y for every opponent in the public live feed,
        // so front/rear boxes use high-frequency relative timing while left/right comes from
        // iRacing's own native spotter state.
        var close = _snapshot.Relatives
            .Where(r => !r.IsPlayer && Math.Abs(r.GapSeconds) <= 1.15)
            .OrderBy(r => Math.Abs(r.GapSeconds))
            .Take(6)
            .ToList();

        int ahead = 0, behind = 0;
        foreach (var r in close)
        {
            bool isAhead = r.GapSeconds > 0;
            int slot = isAhead ? ahead++ : behind++;
            if (slot >= 3) continue;

            double y = isAhead ? cy - 78 - slot * 25 : cy + 58 + slot * 25;
            double x = cx - 32;
            var badge = new Border
            {
                Width = 64, Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = r.ClassBrush,
                BorderBrush = Math.Abs(r.GapSeconds) < .35 ? Theme.Red : new SolidColorBrush(Color.FromArgb(130,0,0,0)),
                BorderThickness = new Thickness(Math.Abs(r.GapSeconds) < .35 ? 2 : 1),
                Child = new TextBlock
                {
                    Text = $"{(string.IsNullOrWhiteSpace(r.Number) ? "CAR" : r.Number)}  {Math.Abs(r.GapSeconds):0.00}",
                    Foreground = Brushes.White,
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(badge, x); Canvas.SetTop(badge, y); _canvas.Children.Add(badge);
        }

        string status;
        Brush statusBrush;
        if (_snapshot.CarLeftRight is 4 or 5 or 6) { status = "THREE WIDE"; statusBrush = Theme.Red; }
        else if (left && right) { status = "BOTH SIDES"; statusBrush = Theme.Red; }
        else if (left) { status = "CAR LEFT"; statusBrush = Theme.Orange; }
        else if (right) { status = "CAR RIGHT"; statusBrush = Theme.Orange; }
        else if (close.Any(r => Math.Abs(r.GapSeconds) < .35)) { status = "CLOSE"; statusBrush = Theme.Red; }
        else if (close.Count > 0) { status = "TRAFFIC"; statusBrush = Theme.Orange; }
        else { status = "CLEAR"; statusBrush = Theme.Green; }

        AddStatus(status, statusBrush, w, h);
    }

    void DrawSide(bool left, int count, double cx, double cy)
    {
        double x = left ? cx - 72 : cx + 42;
        for (int i = 0; i < count; i++)
        {
            var marker = new Border
            {
                Width = 30, Height = 58,
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(220,255,70,70)),
                BorderBrush = Theme.Text,
                BorderThickness = new Thickness(1)
            };
            Canvas.SetLeft(marker, x + (left ? -i * 15 : i * 15));
            Canvas.SetTop(marker, cy - 29 + i * 5);
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

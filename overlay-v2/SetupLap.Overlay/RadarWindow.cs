using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SetupLapOverlay;

public sealed class RadarWindow : WidgetWindow
{
    readonly Canvas _canvas = new();
    OverlaySnapshot? _snapshot;

    public RadarWindow() : base("Radar", 250, 220, (SystemParameters.PrimaryScreenWidth - 250) / 2, SystemParameters.PrimaryScreenHeight - 500)
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
        var car = new Border
        {
            Width = 34, Height = 62,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(210, 255, 77, 23)),
            BorderBrush = Theme.Text, BorderThickness = new Thickness(1)
        };
        Canvas.SetLeft(car, cx - 17); Canvas.SetTop(car, cy - 31); _canvas.Children.Add(car);

        var guide = new Rectangle
        {
            Width = 150, Height = 154,
            Stroke = new SolidColorBrush(Color.FromArgb(45,255,255,255)),
            StrokeThickness = 1,
            RadiusX = 12, RadiusY = 12
        };
        Canvas.SetLeft(guide, cx - 75); Canvas.SetTop(guide, cy - 77); _canvas.Children.Add(guide);

        if (!_snapshot.Connected)
        {
            AddStatus("WAITING FOR IRACING", Theme.Muted, w, h);
            return;
        }

        var me = _snapshot.Relatives.FirstOrDefault(r => r.IsPlayer);
        if (me is null) return;

        // iRacing's public shared-memory data does not expose exact world X/Y for every opponent.
        // V2 therefore uses high-frequency relative gap as a conservative proximity alert.
        // The display intentionally avoids claiming precise left/right placement until that data is available.
        var close = _snapshot.Relatives.Where(r => !r.IsPlayer && Math.Abs(r.GapSeconds) <= 0.8).OrderBy(r => Math.Abs(r.GapSeconds)).Take(4).ToList();

        if (close.Count == 0)
        {
            AddStatus("CLEAR", Theme.Green, w, h);
            return;
        }

        int aheadIndex = 0, behindIndex = 0;
        foreach (var r in close)
        {
            bool ahead = r.GapSeconds > 0;
            int slot = ahead ? aheadIndex++ : behindIndex++;
            double x = slot % 2 == 0 ? cx - 62 : cx + 42;
            double y = ahead ? cy - 72 - (slot/2)*24 : cy + 48 + (slot/2)*24;

            var badge = new Border
            {
                Width = 42, Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = r.ClassBrush,
                BorderBrush = Math.Abs(r.GapSeconds) < 0.35 ? Theme.Red : Theme.Text,
                BorderThickness = new Thickness(Math.Abs(r.GapSeconds) < 0.35 ? 2 : 1),
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(r.Number) ? "CAR" : r.Number,
                    Foreground = Brushes.White,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(badge, x); Canvas.SetTop(badge, y); _canvas.Children.Add(badge);
        }

        AddStatus(close.Any(r => Math.Abs(r.GapSeconds) < 0.35) ? "CLOSE" : "TRAFFIC", close.Any(r => Math.Abs(r.GapSeconds) < 0.35) ? Theme.Red : Theme.Orange, w, h);
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

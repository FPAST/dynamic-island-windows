using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Views;

/// <summary>A small graph of recent values: a line over a soft fill, the newest at the right. Redrawn only when given new values.</summary>
public sealed class Sparkline : FrameworkElement
{
    private double[] _values = [];
    private double _max = 100;
    private Color _color = Colors.White;

    public void Show(double[] values, double max, Color color)
    {
        _values = values; _max = max <= 0 ? 1 : max; _color = color;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), null, new Rect(0, 0, w, h), 4, 4);
        if (_values.Length < 2) return;
        // the full two minutes always span the width: a fresh graph grows in from the right
        int slots = Services.SystemHistory.Samples;
        double step = w / (slots - 1), x0 = w - step * (_values.Length - 1);
        Point P(int i) => new(x0 + i * step, h - 1 - Math.Clamp(_values[i] / _max, 0, 1) * (h - 3));
        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(P(0), false, false);
            for (int i = 1; i < _values.Length; i++) g.LineTo(P(i), true, true);
        }
        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(x0, h), true, true);
            for (int i = 0; i < _values.Length; i++) g.LineTo(P(i), true, false);
            g.LineTo(new Point(w, h), true, false);
        }
        line.Freeze(); fill.Freeze();
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), 4, 4));
        dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(0x60, _color.R, _color.G, _color.B), Color.FromArgb(0x08, _color.R, _color.G, _color.B), 90), null, fill);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(_color), 1.5) { LineJoin = PenLineJoin.Round }, line);
        dc.Pop();
    }
}

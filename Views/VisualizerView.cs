using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Views;

public enum VizStyle { Wave, Bars, Ring }

/// <summary>
/// Draws the spectrum: a soft wave (three layers, each a little lower and fainter), rounded bars, or rays around a
/// circle (for around the cover). Redrawn only when <see cref="Push"/> brings new bands.
/// </summary>
public sealed class VisualizerView : FrameworkElement
{
    private float[] _bands = [];
    public VizStyle Kind { get; set; } = VizStyle.Wave;
    public Color Color { get; set; } = Colors.White;
    /// <summary>Ring: the radius the rays start at (from the centre).</summary>
    public double InnerRadius { get; set; } = 60;

    public VisualizerView() { IsHitTestVisible = false; }

    public void Push(float[] bands)
    {
        if (_bands.Length != bands.Length) _bands = new float[bands.Length];
        Array.Copy(bands, _bands, bands.Length);
        InvalidateVisual();
    }

    public void Clear() { Array.Clear(_bands); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (_bands.Length == 0 || w <= 0 || h <= 0) return;
        switch (Kind)
        {
            case VizStyle.Bars:
            {
                int n = _bands.Length;
                double slot = w / n, bw = Math.Max(1.5, slot * 0.58);
                var brush = new LinearGradientBrush(Color.FromArgb(0xF0, Color.R, Color.G, Color.B), Color.FromArgb(0x70, Color.R, Color.G, Color.B), 90);
                brush.Freeze();
                for (int i = 0; i < n; i++)
                {
                    double bh = Math.Max(2, _bands[i] * h);
                    dc.DrawRoundedRectangle(brush, null, new Rect(i * slot + (slot - bw) / 2, h - bh, bw, bh), bw / 2, bw / 2);
                }
                break;
            }
            case VizStyle.Ring:
            {
                int n = _bands.Length;
                var c = new Point(w / 2, h / 2);
                double maxLen = Math.Max(4, Math.Min(w, h) / 2 - InnerRadius);
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xD0, Color.R, Color.G, Color.B)), 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                pen.Freeze();
                // the spectrum mirrored around the circle: bass at the top, treble at the bottom, on both sides
                for (int i = 0; i < n * 2; i++)
                {
                    int b = i < n ? i : 2 * n - 1 - i;
                    double a = -Math.PI / 2 + Math.PI * 2 * i / (n * 2);
                    double len = 2 + _bands[b] * maxLen;
                    var dir = new Vector(Math.Cos(a), Math.Sin(a));
                    dc.DrawLine(pen, c + dir * InnerRadius, c + dir * (InnerRadius + len));
                }
                break;
            }
            default:
            {
                // three layers: the front one full height and brightest, the back ones lower and fainter
                (double scale, byte alpha)[] layers = [(0.55, 0x40), (0.8, 0x60), (1.0, 0xA8)];
                foreach (var (scale, alpha) in layers)
                {
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(0, h), true, true);
                        var pts = new Point[_bands.Length + 2];
                        pts[0] = new Point(0, h - _bands[0] * h * scale);
                        for (int i = 0; i < _bands.Length; i++) pts[i + 1] = new Point((i + 0.5) / _bands.Length * w, h - _bands[i] * h * scale);
                        pts[^1] = new Point(w, h - _bands[^1] * h * scale);
                        ctx.LineTo(pts[0], true, false);
                        // Catmull-Rom through the points, as Béziers: a smooth line, no corners
                        for (int i = 0; i < pts.Length - 1; i++)
                        {
                            var p0 = pts[Math.Max(0, i - 1)]; var p1 = pts[i]; var p2 = pts[i + 1]; var p3 = pts[Math.Min(pts.Length - 1, i + 2)];
                            var c1 = p1 + (p2 - p0) / 6; var c2 = p2 - (p3 - p1) / 6;
                            ctx.BezierTo(c1, c2, p2, true, false);
                        }
                        ctx.LineTo(new Point(w, h), true, false);
                    }
                    g.Freeze();
                    var fill = new LinearGradientBrush(Color.FromArgb(alpha, Color.R, Color.G, Color.B), Color.FromArgb((byte)(alpha / 4), Color.R, Color.G, Color.B), 90);
                    fill.Freeze();
                    dc.DrawGeometry(fill, null, g);
                }
                break;
            }
        }
    }
}

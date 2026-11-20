using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// Ten vertical sliders (±12 dB) with the response curve drawn over them, in one element (cheap to draw).
/// Drag a band up or down, the wheel nudges by 0.5 dB, a double-click puts it back to 0.
/// </summary>
public sealed class EqView : FrameworkElement
{
    private const double LabelH = 16, ValueH = 14;
    private readonly double[] _gains = new double[EqualizerApo.Bands.Length];
    private int _drag = -1, _hover = -1;
    private Color _accent = Colors.White;

    /// <summary>The gains changed by the user (not by <see cref="SetGains"/>).</summary>
    public event Action<double[]>? Changed;

    public Func<bool> Interactive { get; set; } = () => true;
    public bool Bypassed { get; set { field = value; InvalidateVisual(); } }

    public EqView() { Cursor = Cursors.Hand; Focusable = false; }

    public double[] Gains => (double[])_gains.Clone();

    public void SetGains(IReadOnlyList<double> g)
    {
        for (int i = 0; i < _gains.Length; i++) _gains[i] = i < g.Count ? Math.Clamp(g[i], -EqualizerApo.MaxGain, EqualizerApo.MaxGain) : 0;
        InvalidateVisual();
    }

    public void SetAccent(Color c) { _accent = c; InvalidateVisual(); }

    private double ColW => ActualWidth / _gains.Length;
    private double Top => ValueH + 4;
    private double Bottom => ActualHeight - LabelH - 4;
    private double Y(double db) => Top + (Bottom - Top) * (1 - (db + EqualizerApo.MaxGain) / (2 * EqualizerApo.MaxGain));
    private double Db(double y) => Math.Round(Math.Clamp((1 - (y - Top) / Math.Max(1, Bottom - Top)) * 2 * EqualizerApo.MaxGain - EqualizerApo.MaxGain, -EqualizerApo.MaxGain, EqualizerApo.MaxGain) * 2) / 2;
    private int BandAt(double x) => Math.Clamp((int)(x / Math.Max(1, ColW)), 0, _gains.Length - 1);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, cw = ColW;
        if (w <= 0 || Bottom <= Top) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, ActualHeight));   // the whole area takes the mouse
        double dim = Bypassed ? 0.45 : 1;
        var faint = new Pen(new SolidColorBrush(Color.FromArgb((byte)(0x26 * dim), 0xFF, 0xFF, 0xFF)), 1);
        // 0 dB line and ±6 dB guides
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)(0x45 * dim), 0xFF, 0xFF, 0xFF)), 1) { DashStyle = new DashStyle([2, 3], 0) }, new Point(0, Y(0)), new Point(w, Y(0)));
        foreach (var g in new[] { -6.0, 6.0 }) dc.DrawLine(faint, new Point(0, Y(g)), new Point(w, Y(g)));

        var accent = new SolidColorBrush(Color.FromArgb((byte)(255 * dim), _accent.R, _accent.G, _accent.B));
        var track = new SolidColorBrush(Color.FromArgb((byte)(0x30 * dim), 0xFF, 0xFF, 0xFF));
        var text = new SolidColorBrush(Color.FromArgb((byte)(0xA0 * dim), 0xFF, 0xFF, 0xFF));
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int i = 0; i < _gains.Length; i++)
        {
            double cx = cw * (i + 0.5);
            // the track, the fill from 0 dB to the value, the knob
            dc.DrawRoundedRectangle(track, null, new Rect(cx - 2, Top, 4, Bottom - Top), 2, 2);
            double y0 = Y(0), y = Y(_gains[i]);
            dc.DrawRoundedRectangle(accent, null, new Rect(cx - 2, Math.Min(y, y0), 4, Math.Abs(y - y0)), 2, 2);
            double r = i == _drag ? 8 : i == _hover ? 7 : 6;
            dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)), 1), new Point(cx, y), r, r);
            var label = EqualizerApo.Bands[i] >= 1000 ? $"{EqualizerApo.Bands[i] / 1000}k" : EqualizerApo.Bands[i].ToString(CultureInfo.InvariantCulture);
            var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Variable Text"), 10, text, dpi);
            dc.DrawText(ft, new Point(cx - ft.Width / 2, ActualHeight - LabelH + 2));
            if (i == _drag || i == _hover)
            {
                var v = new FormattedText((_gains[i] > 0 ? "+" : "") + _gains[i].ToString("0.#", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                          new Typeface(new FontFamily("Segoe UI Variable Text"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 10.5, Brushes.White, dpi);
                dc.DrawText(v, new Point(Math.Clamp(cx - v.Width / 2, 0, w - v.Width), 0));
            }
        }

        // the response curve over the sliders, from the first band's centre to the last's
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            double x0 = cw * 0.5, x1 = w - cw * 0.5;
            double lo = Math.Log(EqualizerApo.Bands[0]), hi = Math.Log(EqualizerApo.Bands[^1]), span = Math.Log(1000);
            for (int k = 0; k <= 80; k++)
            {
                double t = k / 80.0;
                // the sliders are evenly spaced, the bands are evenly spaced on a log scale: one maps to the other
                double f = Math.Exp(lo + (hi - lo) * t);
                double db = EqualizerApo.ResponseAt(_gains, Math.Log(f / 20) / span);
                var p = new Point(x0 + (x1 - x0) * t, Y(db));
                if (k == 0) g.BeginFigure(p, false, false); else g.LineTo(p, true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(0xD0 * dim), _accent.R, _accent.G, _accent.B)), 2) { LineJoin = PenLineJoin.Round }, geo);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!Interactive()) return;
        e.Handled = true;
        int i = BandAt(e.GetPosition(this).X);
        if (e.ClickCount == 2) { Set(i, 0); return; }
        _drag = i;
        CaptureMouse();
        Set(i, Db(e.GetPosition(this).Y));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_drag >= 0 && IsMouseCaptured) { Set(_drag, Db(p.Y)); return; }
        int h = BandAt(p.X);
        if (h != _hover) { _hover = h; InvalidateVisual(); }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag < 0) return;
        e.Handled = true;
        _drag = -1;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) { _drag = -1; InvalidateVisual(); }
    protected override void OnMouseLeave(MouseEventArgs e) { _hover = -1; InvalidateVisual(); }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!Interactive()) return;
        e.Handled = true;   // the wheel over the bands moves a band, not the volume
        int i = BandAt(e.GetPosition(this).X);
        Set(i, _gains[i] + (e.Delta > 0 ? 0.5 : -0.5));
    }

    private void Set(int i, double db)
    {
        db = Math.Clamp(db, -EqualizerApo.MaxGain, EqualizerApo.MaxGain);
        if (_gains[i] == db) return;
        _gains[i] = db;
        InvalidateVisual();
        Changed?.Invoke(Gains);
    }
}

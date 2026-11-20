using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// The next hours at a glance: temperature as a line, precipitation as bars (faint for the chance of it, solid for
/// the amount), a mark for "now", hours along the bottom and the warmest and coldest values on the line.
/// </summary>
public sealed class WeatherChart : FrameworkElement
{
    private IReadOnlyList<WeatherHour> _hours = [];
    private DateTime _now;

    public int Span { get; init; } = 12;

    public void Show(IReadOnlyList<WeatherHour> hourly, DateTime now)
    {
        var from = now.Date.AddHours(now.Hour);
        _hours = hourly.Where(h => h.Time >= from && h.Time <= from.AddHours(Span)).OrderBy(h => h.Time).ToList();
        _now = now;
        InvalidateVisual();
    }

    private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (_hours.Count < 2 || w <= 0 || h <= 0) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double labelH = 14, topPad = 16;
        double plotTop = topPad, plotBottom = h - labelH - 2, plotH = plotBottom - plotTop;
        var t0 = _hours[0].Time; double spanH = (_hours[^1].Time - t0).TotalHours;
        double X(DateTime t) => (t - t0).TotalHours / spanH * w;

        // precipitation bars, along the bottom third
        double slot = w / spanH, barW = Math.Max(2, slot * 0.5);
        var chanceBrush = new SolidColorBrush(Color.FromArgb(0x48, 0xB4, 0xDC, 0xFF));
        var rainBrush = new SolidColorBrush(Color.FromArgb(0xF0, 0x8C, 0xCC, 0xFF));
        foreach (var hr in _hours.Take(_hours.Count - 1))
        {
            double x = X(hr.Time) + slot / 2 - barW / 2;
            if (hr.Chance > 0) { double ch = plotH * 0.35 * hr.Chance / 100; dc.DrawRoundedRectangle(chanceBrush, null, new Rect(x, plotBottom - ch, barW, ch), 1.5, 1.5); }
            // 2 mm an hour is already proper rain: the bar is full there
            if (hr.Precip > 0.05) { double ph = Math.Max(4, plotH * 0.35 * Math.Min(1, hr.Precip / 2)); dc.DrawRoundedRectangle(rainBrush, null, new Rect(x, plotBottom - ph, barW, ph), 1.5, 1.5); }
        }

        // temperature line in the upper part
        double min = _hours.Min(x => x.Temp), max = _hours.Max(x => x.Temp);
        if (max - min < 4) { double mid = (max + min) / 2; min = mid - 2; max = mid + 2; }
        double Y(double t) => plotTop + (1 - (t - min) / (max - min)) * plotH * 0.62;
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            var pts = _hours.Select(x => new Point(X(x.Time), Y(x.Temp))).ToList();
            g.BeginFigure(pts[0], false, false);
            for (int i = 1; i < pts.Count; i++)
            {
                // a gentle curve through the points (Catmull–Rom as Bézier)
                var p0 = pts[Math.Max(0, i - 2)]; var p1 = pts[i - 1]; var p2 = pts[i]; var p3 = pts[Math.Min(pts.Count - 1, i + 1)];
                g.BezierTo(new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6), new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6), p2, true, true);
            }
        }
        line.Freeze();
        dc.DrawGeometry(null, new Pen(Brushes.White, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, line);

        // the warmest and coldest hours, labelled
        var dim = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
        var hot = _hours.MaxBy(x => x.Temp)!; var cold = _hours.MinBy(x => x.Temp)!;
        foreach (var (hr, above) in new[] { (hot, true), (cold, false) })
        {
            if (!above && ReferenceEquals(hot, cold)) break;
            var ft = new FormattedText($"{Math.Round(hr.Temp)}°", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 10.5, Brushes.White, dpi);
            double x = Math.Clamp(X(hr.Time) - ft.Width / 2, 0, w - ft.Width), y = Y(hr.Temp) + (above ? -ft.Height - 1 : 3);
            dc.DrawText(ft, new Point(x, Math.Clamp(y, 0, plotBottom - ft.Height)));
            dc.DrawEllipse(Brushes.White, null, new Point(X(hr.Time), Y(hr.Temp)), 2.5, 2.5);
        }

        // now
        if (_now >= t0 && _now <= _hours[^1].Time)
        {
            double nx = X(_now);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), 1) { DashStyle = new DashStyle([2, 2], 0) }, new Point(nx, plotTop - 4), new Point(nx, plotBottom));
        }

        // hours along the bottom, every few hours
        int step = spanH > 14 ? 6 : 3;
        foreach (var hr in _hours.Where(x => x.Time.Hour % step == 0))
        {
            var ft = new FormattedText(hr.Time.ToString("H:mm", CultureInfo.InvariantCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 9.5, dim, dpi);
            double x = Math.Clamp(X(hr.Time) - ft.Width / 2, 0, w - ft.Width);
            dc.DrawText(ft, new Point(x, h - labelH));
        }
    }
}

/// <summary>The sun's path today: an arc from sunrise to sunset with the sun on it now (below the line at night).</summary>
public sealed class SunArc : FrameworkElement
{
    private DateTime _rise, _set, _now;

    public void Show(DateTime rise, DateTime set, DateTime now) { _rise = rise; _set = set; _now = now; InvalidateVisual(); }

    /// <summary>Where the sun is on the arc: 0 at sunrise, 1 at sunset; outside 0..1 at night.</summary>
    public static double Progress(DateTime rise, DateTime set, DateTime now) =>
        set > rise ? (now - rise).TotalMinutes / (set - rise).TotalMinutes : -1;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0 || _set <= _rise) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        const double labelH = 14;
        double baseY = h - labelH - 4, r = Math.Min(w / 2 - 8, baseY - 6), cx = w / 2;
        var faint = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        dc.DrawLine(new Pen(faint, 1), new Point(cx - r - 6, baseY), new Point(cx + r + 6, baseY));
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(new Point(cx - r, baseY), false, false);
            g.ArcTo(new Point(cx + r, baseY), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        arc.Freeze();
        dc.DrawGeometry(null, new Pen(faint, 1.5) { DashStyle = new DashStyle([3, 3], 0) }, arc);

        double p = Progress(_rise, _set, _now);
        bool day = p is >= 0 and <= 1;
        double a = Math.PI * (1 - Math.Clamp(p, 0, 1));
        var sun = new Point(cx + r * Math.Cos(a), baseY - r * Math.Sin(a));
        if (day)
        {
            // the part already travelled, bright
            var done = new StreamGeometry();
            using (var g = done.Open())
            {
                g.BeginFigure(new Point(cx - r, baseY), false, false);
                g.ArcTo(sun, new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            }
            done.Freeze();
            var warm = Color.FromRgb(0xFF, 0xC8, 0x4A);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(warm), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, done);
            dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0x90, warm.R, warm.G, warm.B), Color.FromArgb(0, warm.R, warm.G, warm.B)), null, sun, 12, 12);
            dc.DrawEllipse(new SolidColorBrush(warm), null, sun, 5.5, 5.5);
        }
        var face = new Typeface(new FontFamily("Segoe UI Variable Text"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var dim = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF));
        var rise = new FormattedText("↑ " + _rise.ToString("H:mm", CultureInfo.InvariantCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 10.5, dim, dpi);
        var set = new FormattedText(_set.ToString("H:mm", CultureInfo.InvariantCulture) + " ↓", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 10.5, dim, dpi);
        dc.DrawText(rise, new Point(Math.Max(0, cx - r - rise.Width / 2), h - labelH));
        dc.DrawText(set, new Point(Math.Min(w - set.Width, cx + r - set.Width / 2), h - labelH));
    }
}

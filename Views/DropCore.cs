using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// The circle of the island's file zone (after designbookmark's File drop): it wakes up when a file comes near
/// (lifts, its fill brightens, the arrow bobs), gulps the file on the drop, shows work on a ring around a rolling
/// percentage, and at the end floods green while a check draws itself. Springs from drop.js. Drawn in code;
/// frames only while something moves.
/// </summary>
public sealed class DropCore : FrameworkElement
{
    private sealed class S(double x, double k, double c)
    {
        public double X = x, V, T = x;
        public readonly double K = k, C = Motion.Reduced ? 2 * Math.Sqrt(k) : c;
        public void Step(double dt) { double a = -K * (X - T) - C * V; V += a * dt; X += V * dt; }
        public bool Still => Math.Abs(X - T) < 0.002 && Math.Abs(V) < 0.02;
    }

    // drop.js: the zone's lift 300 / 26, the circle 420 / 14, arrow and percent 420 / 30, ring 300 / 30, green 260 / 21, check 200 / 26
    private readonly S _near = new(0, 300, 26), _scale = new(1, 420, 14), _arrow = new(1, 420, 30), _pct = new(0, 420, 30),
                       _ring = new(0, 300, 30), _green = new(0, 260, 21), _check = new(0, 200, 26);
    private double _progress, _shown, _clock;
    private bool _working, _finishing, _ticking;
    private string _glyph = "";   // the arrow up
    private TimeSpan _lastFrame;
    private Action? _whenFull;

    public DropCore()
    {
        Width = Height = 48;
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => { if (IsVisible) Start(); else Stop(); };
    }

    /// <summary>The icon in the middle at rest (an arrow while waiting, the file's kind once it is in).</summary>
    public string Glyph { get => _glyph; set { _glyph = value; InvalidateVisual(); } }

    /// <summary>A file is over the island (or not): the circle lifts and its arrow bobs.</summary>
    public void Near(bool near) { _near.T = near ? 1 : 0; Start(); }

    /// <summary>The file is in: a gulp.</summary>
    public void Gulp() { _near.T = 0; if (!Motion.Reduced) _scale.X = 1.2; Start(); }

    /// <summary>Work begins: the icon gives way to a percentage on a ring.</summary>
    public void Begin()
    {
        _working = true; _finishing = false; _progress = _shown = 0;
        _arrow.T = 0; _pct.T = 1; _ring.T = 1; _green.T = 0; _check.T = 0;
        if (!Motion.Reduced) _scale.X = 1.2;
        Start();
    }

    /// <summary>How far the work is, 0…1. The ring follows at most a full turn in 0.6 s, so instant work still shows.</summary>
    public void SetProgress(double p) { _progress = Math.Clamp(p, 0, 1); Start(); }

    /// <summary>The work is done: once the ring is full, green floods in and the check draws itself; then the callback.</summary>
    public void Finish(Action? then = null)
    {
        _progress = 1; _finishing = true; _whenFull = then;
        Start();
    }

    /// <summary>Back to waiting.</summary>
    public void Reset()
    {
        _working = _finishing = false; _whenFull = null; _progress = _shown = 0;
        _arrow.T = 1; _pct.T = 0; _ring.T = 0; _green.T = 0; _check.T = 0; _near.T = 0;
        Start();
    }

    /// <summary>Tests: the ring's fill, the green flood, the check, whether frames run.</summary>
    internal (double Ring, double Green, double Check, bool Moving) TestState => (_shown, _green.X, _check.X, _ticking);

    private void Start()
    {
        if (_ticking || !IsVisible) { InvalidateVisual(); return; }
        _ticking = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void Stop()
    {
        if (!_ticking) return;
        _ticking = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 1 / 30.0);
        _lastFrame = now;
        _clock += dt;
        if (_working) _shown = Motion.Reduced ? _progress : Math.Min(_progress, _shown + dt / 0.6);
        if (_finishing && _shown >= 0.999)
        {
            // full: the percentage and ring go, green floods in, the circle settles a little smaller, the check draws
            _finishing = false; _working = false;
            _pct.T = 0; _ring.T = 0; _green.T = 1; _check.T = 1;
            if (!Motion.Reduced) _scale.X = .84;
            var then = _whenFull; _whenFull = null;
            then?.Invoke();
        }
        var all = new[] { _near, _scale, _arrow, _pct, _ring, _green, _check };
        for (int i = 0; i < 4; i++) foreach (var s in all) s.Step(dt / 4);
        InvalidateVisual();
        bool bobbing = _near.T > 0.5 && !Motion.Reduced;
        if (!bobbing && !_working && !_finishing && all.All(s => s.Still)) Stop();
    }

    private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight), r = size / 2, h = Math.Clamp(_near.X, 0, 1);
        var c = new Point(ActualWidth / 2, ActualHeight / 2 - 5 * h * size / 72);   // lifts a little while a file is near
        var fg = Res("Fg", Brushes.White);
        dc.PushTransform(new ScaleTransform(_scale.X, _scale.X, c.X, c.Y));

        // the circle: the chip colour, brighter while a file is near
        dc.DrawEllipse(Res("ChipBg", Brushes.DimGray), null, c, r, r);
        if (h > 0.01) { dc.PushOpacity(h); dc.DrawEllipse(Res("ChipBg", Brushes.DimGray), null, c, r, r); dc.Pop(); }

        // green flooding out from the middle
        var green = new SolidColorBrush(Color.FromRgb(0x34, 0xD1, 0x7A));
        if (_green.X > 0.005) dc.DrawEllipse(green, null, c, r * Math.Max(0, _green.X), r * Math.Max(0, _green.X));

        // the ring: a faint track and the part done, from 12 o'clock clockwise
        double ro = Math.Clamp(_ring.X, 0, 1), rr = r - 2;
        if (ro > 0.01)
        {
            dc.PushOpacity(ro);
            dc.DrawEllipse(null, new Pen(Res("TrackBg", Brushes.Gray), 3), c, rr, rr);
            if (_shown > 0.002)
            {
                var pen = new Pen(fg, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (_shown >= 0.999) dc.DrawEllipse(null, pen, c, rr, rr);
                else
                {
                    double a = _shown * 2 * Math.PI;
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(c.X, c.Y - rr), false, false);
                        ctx.ArcTo(new Point(c.X + rr * Math.Sin(a), c.Y - rr * Math.Cos(a)), new Size(rr, rr), 0, _shown > .5, SweepDirection.Clockwise, true, false);
                    }
                    dc.DrawGeometry(null, pen, g);
                }
            }
            dc.Pop();
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // the icon (arrow at rest): bobs while a file is near, rises away when work begins
        double ao = Math.Clamp(_arrow.X, 0, 1);
        if (ao > 0.01)
        {
            double bob = -3.5 * h * (.5 + .5 * Math.Sin(_clock * 7));
            var icon = new FormattedText(_glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size * 0.36, fg, dpi);
            double s = .6 + .4 * _arrow.X;
            dc.PushOpacity(ao);
            dc.PushTransform(new TranslateTransform(0, bob + (1 - _arrow.X) * -12));
            dc.PushTransform(new ScaleTransform(s, s, c.X, c.Y));
            dc.DrawText(icon, new Point(c.X - icon.Width / 2, c.Y - icon.Height / 2));
            dc.Pop(); dc.Pop(); dc.Pop();
        }

        // the percentage
        double po = Math.Clamp(_pct.X, 0, 1);
        if (po > 0.01)
        {
            var t = new FormattedText(Math.Round(_shown * 100).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), size * 0.27, fg, dpi);
            double s = .7 + .3 * _pct.X;
            dc.PushOpacity(po);
            dc.PushTransform(new ScaleTransform(s, s, c.X, c.Y));
            dc.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
            dc.Pop(); dc.Pop();
        }

        // the check drawing itself, dark on the green
        double k = Math.Clamp(_check.X, 0, 1);
        if (k > 0.01)
        {
            double u = size / 24 * 0.62;   // the original's 30 px check in a 72 px circle
            var p1 = new Point(c.X + (5 - 12) * u, c.Y + (12.5 - 12) * u);
            var p2 = new Point(c.X + (9.5 - 12) * u, c.Y + (17 - 12) * u);
            var p3 = new Point(c.X + (19 - 12) * u, c.Y + (7.5 - 12) * u);
            double l1 = (p2 - p1).Length, l2 = (p3 - p2).Length, drawn = (l1 + l2) * k;
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0C)), 2.4 * u) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.PushOpacity(Math.Clamp(k * 4, 0, 1));
            if (drawn <= l1) dc.DrawLine(pen, p1, p1 + (p2 - p1) * (drawn / l1));
            else
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(p1, false, false);
                    ctx.LineTo(p2, true, true);
                    ctx.LineTo(p2 + (p3 - p2) * ((drawn - l1) / l2), true, true);
                }
                dc.DrawGeometry(null, pen, g);
            }
            dc.Pop();
        }
        dc.Pop();
    }
}

/// <summary>
/// A line of text that rolls over to new text (after File drop's two lines): the old one leaves upward while the
/// new one rises in, waiting for the old one to clear. Font, size and colour come from the surroundings.
/// </summary>
public sealed class RollingLine : Grid
{
    private readonly TextBlock _a = new() { TextTrimming = TextTrimming.CharacterEllipsis }, _b = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private TextBlock _cur, _old;
    private readonly TranslateTransform _curMove = new(), _oldMove = new();
    private double _x = 1, _v;   // 0 → 1: the roll (drop.js: 360 / 30)
    private bool _ticking;
    private TimeSpan _lastFrame;

    public RollingLine()
    {
        ClipToBounds = true;
        _cur = _a; _old = _b;
        _a.RenderTransform = _curMove; _b.RenderTransform = _oldMove;
        _b.Opacity = 0;
        Children.Add(_b); Children.Add(_a);
    }

    public string Text
    {
        get => _cur.Text;
        set
        {
            if (value == _cur.Text) return;
            if (!IsLoaded || !IsVisible || Motion.Reduced) { _cur.Text = value; _old.Opacity = 0; _cur.Opacity = 1; _curMove.Y = 0; _x = 1; return; }
            (_cur, _old) = (_old, _cur);
            _cur.Text = value;
            _cur.RenderTransform = _curMove; _old.RenderTransform = _oldMove;
            _x = 0; _v = 0;
            if (!_ticking) { _ticking = true; _lastFrame = TimeSpan.Zero; CompositionTarget.Rendering += OnFrame; }
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 1 / 30.0);
        _lastFrame = now;
        for (int i = 0; i < 4; i++) { double a = -360 * (_x - 1) - 30 * _v; _v += a * dt / 4; _x += _v * dt / 4; }
        double o = Math.Clamp(_x, 0, 1), rise = Math.Max(ActualHeight, 16);
        _cur.Opacity = Math.Clamp((o - .35) / .65, 0, 1);
        _curMove.Y = (1 - _x) * rise;
        _old.Opacity = Math.Clamp(1 - o * 1.8, 0, 1);
        _oldMove.Y = -_x * rise;
        if (Math.Abs(_x - 1) < 0.002 && Math.Abs(_v) < 0.02)
        {
            _x = 1; _cur.Opacity = 1; _curMove.Y = 0; _old.Opacity = 0;
            _ticking = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// A "+" that pours out liquid bubbles (after designbookmark's Goo menu): the bubbles fly out on springs in a
/// fan above it, fused to it like liquid while they are close; one can be dragged out on a rubber band and
/// springs home; picking one pulls them all back in, and the "+" morphs into a pill that says what happened.
/// Drawn in code (the browser's blur-and-threshold goo is the metaball outline here); the bubbles reach outside
/// its own small box. Frames only while something moves.
/// </summary>
public sealed class GooMenu : FrameworkElement
{
    // tokens.css, Goo menu (stiffness, damping; mass 1)
    private static readonly (double K, double C) Out = (230, 15), In = (520, 34), Back = (300, 12), Toasting = (260, 17);
    private const double Stagger = 0.045, ToastSeconds = 2.6;

    private sealed class S(double x, double k = 400, double c = 30)
    {
        public double X = x, V, T = x, K = k, C = c;
        public void Tune(double k, double c) { K = k; C = Motion.Reduced ? 2 * Math.Sqrt(k) : c; }
        public void Step(double dt) { double a = -K * (X - T) - C * V; V += a * dt; X += V * dt; }
        public bool Still(double eps) => Math.Abs(X - T) < eps && Math.Abs(V) < eps * 20;
    }

    private sealed class Bubble(string glyph, string name)
    {
        public readonly string Glyph = glyph, Name = name;
        public readonly S X = new(0), Y = new(0), Scale = new(.5, 420, 22);
        public double Wait = -1;           // a launch or return waiting for its turn (the stagger)
        public Point Goal; public (double K, double C) Spring;
        public bool Hover, Pressed, Dragging; public Point PressAt;
        public Point Home;
        public double Angle;
    }

    private readonly double _size, _bubble, _radius;
    private readonly Bubble[] _bubbles;
    private readonly S _scale = new(1, 520, 16), _width, _turn = new(0, 320, 20);
    private bool _open, _ticking;
    private double _toastLeft = -1;
    private FormattedText? _toastText;
    private string _toastGlyph = "";
    private TimeSpan _lastFrame;

    /// <summary>The area the bubbles must stay inside (the page): the fan turns away from a near edge.</summary>
    public FrameworkElement? Bounds { get; set; }

    /// <summary>A bubble was picked (its index); the menu has closed itself.</summary>
    public event Action<int>? Picked;

    /// <param name="size">The "+"'s diameter (the original's 64; the island's app icons are 38).</param>
    public GooMenu(double size, IReadOnlyList<(string Glyph, string Name)> actions)
    {
        _size = size;
        _bubble = Math.Round(size * 0.9);          // the original's 48 next to 64, a touch larger to stay clickable
        _radius = Math.Round(size * 2.2);          // the original's 112 next to 64, closer: the island has less room
        _width = new S(size, 300, 20);
        // a fan above the "+", from up-left to up-right (the original's -160°, -117°, -63°, -20°)
        double[] fan = actions.Count == 4 ? [-160, -117, -63, -20] : Enumerable.Range(0, actions.Count).Select(i => -160 + 140.0 * i / Math.Max(1, actions.Count - 1)).ToArray();
        _bubbles = actions.Select((a, i) => new Bubble(a.Glyph, a.Name) { Angle = fan[i] }).ToArray();
        Aim(fan);
        Width = size; Height = size;
        Cursor = Cursors.Hand;
        // hidden (the island folds, the page changes): closed; back (a rebuilt app row): a running toast goes on
        IsVisibleChanged += (_, _) => { if (!IsVisible) { Close(instant: true); Stop(); } else if (_toastLeft > 0) Start(); };
    }

    private Point Center => new(_size / 2, _size / 2);

    /// <summary>Each bubble's home on the fan, at these angles (0° is right, -90° straight up).</summary>
    private void Aim(double[] angles)
    {
        for (int i = 0; i < _bubbles.Length; i++)
        {
            double a = angles[Math.Min(i, angles.Length - 1)] * Math.PI / 180;
            _bubbles[i].Home = new Point(Math.Cos(a) * _radius, Math.Sin(a) * _radius);
        }
    }

    /// <summary>
    /// The original fans out symmetrically upwards (-160°…-20°). Near a side of the page the bubbles would leave
    /// it, so the fan opens away from that side instead — from straight up to the right by the left edge, the
    /// mirror by the right one — never below the "+" (turning the whole fan sent the last bubble down off the page).
    /// </summary>
    private void AimInside()
    {
        int n = _bubbles.Length;
        double[] Spread(double from, double to) => Enumerable.Range(0, n).Select(i => from + (to - from) * i / Math.Max(1, n - 1)).ToArray();
        var angles = _bubbles.Select(b => b.Angle).ToArray();
        if (Bounds is { ActualWidth: > 0 } area && IsDescendantOf(area))
        {
            var p = TranslatePoint(Center, area);
            double need = _radius + _bubble / 2 + 4;
            if (p.X < need) angles = Spread(-100, -10);
            else if (area.ActualWidth - p.X < need) angles = Spread(-170, -80);
        }
        Aim(angles);
    }

    public bool IsOpen => _open;

    // ------------------------------------------------------------------ open, close, pick, toast

    public void Toggle() => SetOpen(!_open);

    private void SetOpen(bool open)
    {
        if (open == _open) return;
        _open = open;
        if (open) AimInside();
        _turn.T = open ? 135 : 0;
        _scale.V -= open ? 3 : 1.6;
        int n = _bubbles.Length;
        for (int i = 0; i < n; i++)
        {
            var b = _bubbles[i];
            b.Hover = b.Pressed = b.Dragging = false;
            if (open)
            {
                b.Wait = i * Stagger; b.Goal = b.Home; b.Spring = Out;
                b.Scale.T = 1; b.Scale.Tune(300, 14);
            }
            else
            {
                b.Wait = (n - 1 - i) * Stagger * .66; b.Goal = new Point(0, 0); b.Spring = In;
                b.Scale.T = .5; b.Scale.Tune(420, 30);
            }
        }
        if (open) Window.GetWindow(this)?.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OutsidePress), true);
        else Window.GetWindow(this)?.RemoveHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OutsidePress));
        Start();
    }

    private void Close(bool instant)
    {
        if (!_open) return;
        SetOpen(false);
        if (!instant) return;
        foreach (var b in _bubbles) { b.Wait = -1; b.X.X = b.X.T = b.Y.X = b.Y.T = 0; b.X.V = b.Y.V = 0; b.Scale.X = b.Scale.T = .5; }
    }

    /// <summary>A press anywhere else in the window closes the menu.</summary>
    private void OutsidePress(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && (d == this || IsAncestorOf(d))) return;
        if (Hit(e.GetPosition(this)) >= -1) return;
        Close(instant: false);
    }

    /// <summary>The "+" becomes a pill saying what happened, for 2.6 s.</summary>
    public void Toast(string glyph, string text)
    {
        var face = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        _toastText = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 13, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = 260, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        _toastGlyph = glyph;
        _width.Tune(Toasting.K, Toasting.C);
        _width.T = _toastText.WidthIncludingTrailingWhitespace + 18 + 34;   // the original's text + 44, with room for the icon
        _scale.V -= 2;
        _toastLeft = ToastSeconds;
        Start();
    }

    private void EndToast()
    {
        _toastLeft = -1;
        _width.Tune(320, 22);
        _width.T = _size;
        _scale.V -= 1.5;
        Start();
    }

    // ------------------------------------------------------------------ pointer

    /// <summary>-2 nothing, -1 the "+", or the bubble's index.</summary>
    private int Hit(Point p)
    {
        if (_open)
            for (int i = 0; i < _bubbles.Length; i++)
            {
                var b = _bubbles[i];
                var c = new Point(Center.X + b.X.X, Center.Y + b.Y.X);
                if ((p - c).Length <= _bubble / 2 * Math.Max(b.Scale.X, .6)) return i;
            }
        return new Rect(0, 0, _width.X, _size).Contains(p) ? -1 : -2;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters p) => Hit(p.HitPoint) >= -1 ? new PointHitTestResult(this, p.HitPoint) : null;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        int hit = Hit(p);
        for (int i = 0; i < _bubbles.Length; i++)
        {
            var b = _bubbles[i];
            if (b.Pressed)
            {
                var d = p - b.PressAt;
                if (!b.Dragging && d.Length > 4) { b.Dragging = true; b.Scale.T = 1.08; b.Hover = false; }
                if (!b.Dragging) continue;
                // a rubber band: the further it is pulled, the less it follows
                double r = d.Length, k = r > 0 ? 70 * (1 - 1 / (r * .55 / 70 + 1)) / r : 0;
                b.Wait = -1;
                b.X.Tune(900, 50); b.Y.Tune(900, 50);
                b.X.T = b.Home.X + d.X * k; b.Y.T = b.Home.Y + d.Y * k;
                Start();
                continue;
            }
            bool over = _open && hit == i;
            if (over == b.Hover) continue;
            b.Hover = over;
            b.Scale.T = over ? 1.14 : 1;
            if (over) b.Scale.Tune(500, 18);
            Start();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        foreach (var b in _bubbles) if (b.Hover && !b.Pressed) { b.Hover = false; if (_open) b.Scale.T = 1; }
        Start();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        int hit = Hit(e.GetPosition(this));
        if (hit < -1) return;
        e.Handled = true;
        if (hit == -1)
        {
            if (_toastLeft > 0) EndToast();
            SetOpen(!_open);
            return;
        }
        var b = _bubbles[hit];
        b.Pressed = true; b.Dragging = false; b.PressAt = e.GetPosition(this);
        b.Scale.T = .92;
        CaptureMouse();
        Start();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var b = _bubbles.FirstOrDefault(x => x.Pressed);
        if (b == null) return;
        e.Handled = true;
        bool dragged = b.Dragging;
        b.Pressed = b.Dragging = false;
        ReleaseMouseCapture();
        if (dragged)
        {
            // let go: home with a wobble
            b.X.Tune(Back.K, Back.C); b.Y.Tune(Back.K, Back.C);
            b.X.T = b.Home.X; b.Y.T = b.Home.Y; b.Scale.T = 1;
            Start();
            return;
        }
        _ = Pick(Array.IndexOf(_bubbles, b));
    }

    private async Task Pick(int i)
    {
        var b = _bubbles[i];
        b.Scale.T = 1; b.Scale.V += 4;
        Start();
        await Task.Delay(80);
        SetOpen(false);
        await Task.Delay(240);
        Picked?.Invoke(i);
    }

    /// <summary>Tests (no mouse is moved): open, pick, and where everything is.</summary>
    internal void TestPick(int i) => _ = Pick(i);
    internal (bool Open, double Turn, double Width, double FarthestBubble, bool Moving) TestState =>
        (_open, _turn.X, _width.X, _bubbles.Max(b => Math.Sqrt(b.X.X * b.X.X + b.Y.X * b.Y.X)), _ticking);

    // ------------------------------------------------------------------ frames (only while something moves)

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
        if (_toastLeft > 0 && (_toastLeft -= dt) <= 0) EndToast();
        foreach (var b in _bubbles)
            if (b.Wait >= 0 && (b.Wait -= dt) < 0)
            {
                b.X.Tune(b.Spring.K, b.Spring.C); b.Y.Tune(b.Spring.K, b.Spring.C);
                b.X.T = b.Goal.X; b.Y.T = b.Goal.Y;
            }
        for (int s = 0; s < 4; s++)
        {
            _scale.Step(dt / 4); _width.Step(dt / 4); _turn.Step(dt / 4);
            foreach (var b in _bubbles) { b.X.Step(dt / 4); b.Y.Step(dt / 4); b.Scale.Step(dt / 4); }
        }
        InvalidateVisual();
        bool still = _toastLeft <= 0 && _scale.Still(0.002) && _width.Still(0.2) && _turn.Still(0.2)
                     && _bubbles.All(b => b.Wait < 0 && !b.Pressed && b.X.Still(0.2) && b.Y.Still(0.2) && b.Scale.Still(0.002));
        if (still) Stop();
    }

    // ------------------------------------------------------------------ drawing

    private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    /// <summary>
    /// The island's chip colour made solid over the island's own colour: the original's bubbles are a solid grey,
    /// and the see-through chip colour let the page show through the drops (a cover through a bubble).
    /// </summary>
    private Brush Solid(string key)
    {
        var top = (TryFindResource(key) as SolidColorBrush)?.Color ?? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
        var under = (TryFindResource("IslandBg") as SolidColorBrush)?.Color ?? Colors.Black;
        double a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
        return new SolidColorBrush(Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B)));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var blob = Solid(IsMouseOver && !_open && _toastLeft <= 0 ? "TileBgHover" : "ChipBg");
        var fg = Res("Fg", Brushes.White);
        var c = Center;
        double ms = _scale.X, r = _size / 2;
        var icon = new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText Glyph(string g, double size, Brush b) => new(g, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, icon, size, b, dpi);

        // the bubbles first, under the "+": squashed and stretched along their flight, joined to it while close
        foreach (var b in _bubbles)
        {
            var bc = new Point(c.X + b.X.X, c.Y + b.Y.X);
            double br = _bubble / 2 * b.Scale.X;
            double dist = (bc - c).Length;
            if (br < 1 || dist < 1 && b.Scale.X <= .51) continue;
            if (Goo.Neck(c, r * ms, bc, br) is { } neck) dc.DrawGeometry(blob, null, neck);
            double sp = Math.Sqrt(b.X.V * b.X.V + b.Y.V * b.Y.V), st = 1 + Math.Min(sp / 2600, .32), ang = Math.Atan2(b.Y.V, b.X.V) * 180 / Math.PI;
            dc.PushTransform(new RotateTransform(ang, bc.X, bc.Y));
            dc.DrawEllipse(blob, null, bc, br * st, br / st);
            dc.Pop();
            // the icon shows once the bubble is out of the "+"
            double alpha = Math.Clamp((dist - 18) / 40, 0, 1);
            if (alpha <= 0.01) continue;
            dc.PushOpacity(alpha);
            var g = Glyph(b.Glyph, 15 * b.Scale.X, fg);
            dc.DrawText(g, new Point(bc.X - g.Width / 2, bc.Y - g.Height / 2));
            if (b.Hover && !b.Dragging) Tip(dc, b.Name, new Point(bc.X, bc.Y - br - 8), fg);
            dc.Pop();
        }

        // the "+" (or the pill it morphs into), anchored on the left: it grows to the right, into free space
        double w = Math.Max(_size, _width.X);
        var pill = new Rect(c.X - r, c.Y - r, w, _size);
        dc.PushTransform(new ScaleTransform(ms, ms, c.X, c.Y));
        dc.DrawRoundedRectangle(blob, null, pill, r, r);
        double toast = Math.Clamp((_width.X - _size) / 24, 0, 1);
        if (toast < 0.99)
        {
            dc.PushOpacity(1 - toast);
            dc.PushTransform(new RotateTransform(_turn.X, c.X, c.Y));
            var plus = Glyph("", 15, Res("Fg2", fg));
            dc.DrawText(plus, new Point(c.X - plus.Width / 2, c.Y - plus.Height / 2));
            dc.Pop(); dc.Pop();
        }
        if (toast > 0.01 && _toastText != null)
        {
            dc.PushOpacity(toast);
            var g = Glyph(_toastGlyph, 14, fg);
            dc.DrawText(g, new Point(pill.X + 14, c.Y - g.Height / 2));
            _toastText.SetForegroundBrush(fg);
            dc.PushClip(new RectangleGeometry(new Rect(pill.X + 14 + g.Width + 8, pill.Y, Math.Max(0, w - 36 - g.Width), _size)));
            dc.DrawText(_toastText, new Point(pill.X + 14 + g.Width + 8, c.Y - _toastText.Height / 2));
            dc.Pop(); dc.Pop();
        }
        dc.Pop();
    }

    /// <summary>The bubble's name above it while the mouse is on it.</summary>
    private void Tip(DrawingContext dc, string text, Point bottomCentre, Brush fg)
    {
        var face = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        var t = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 11.5, fg, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var box = new Rect(bottomCentre.X - t.Width / 2 - 9, bottomCentre.Y - t.Height - 10, t.Width + 18, t.Height + 10);
        dc.DrawRoundedRectangle(Res("IslandBg", Brushes.Black), new Pen(Res("TileStroke", Brushes.Gray), 1), box, box.Height / 2, box.Height / 2);
        dc.DrawText(t, new Point(box.X + 9, box.Y + 5));
    }
}

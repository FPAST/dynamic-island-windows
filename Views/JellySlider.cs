using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// A slider whose thumb is jelly (after designbookmark's Jelly slider): it follows the pointer on a spring and
/// stretches with its speed; while dragging, a drop with the value rises out of it, leaning the way it is dragged,
/// joined to the thumb like liquid; past either end it meets rubber and snaps back. Still a Slider: Value,
/// ValueChanged, Minimum / Maximum and snapping to ticks work as before. Drawn in code; it animates only while
/// something moves.
/// </summary>
public sealed class JellySlider : Slider
{
    // springs from tokens.css (stiffness, damping; mass 1), stepped like motion.js
    private static readonly (double K, double C) Follow = (900, 50), Bubble = (420, 22), Rebound = (300, 14), Glide = (420, 34);

    private sealed class S(double x, double k, double c)
    {
        public double X = x, V, T = x, K = k, C = c;
        public void Tune((double K, double C) s) { K = s.K; C = Motion.Reduced ? 2 * Math.Sqrt(s.K) : s.C; }
        public void Step(double dt) { double a = -K * (X - T) - C * V; V += a * dt; X += V * dt; }
        public bool Still(double eps) => Math.Abs(X - T) < eps && Math.Abs(V) < eps * 10;
    }

    // the island's sizes: a 20 px thumb (the original's 24 is a touch big next to the island's rows), a 30 px drop
    private const double R = 10, D = 15, Rail = 24, Rise = -32, RestDrop = 0.5, ScaleHeight = 30;

    private readonly S _x, _dx, _dy, _ds, _ps;
    private bool _dragging, _hover, _lifted, _ticking, _setting;
    private TimeSpan _lastFrame;

    /// <summary>The scale under the track: a mark every 5 %, a taller one with a number every 25 %.</summary>
    public bool ShowScale { get; set; }
    /// <summary>The number in the drop and under the scale (default: whole numbers, or percent for a 0…1 range).</summary>
    public Func<double, string>? Format { get; set; }
    /// <summary>Asked when a drag starts; false leaves the press to whatever is under the slider (a widget being moved).</summary>
    public Func<bool>? CanDrag { get; set; }
    /// <summary>The thumb is being held (by the mouse, or a test): outside values must wait.</summary>
    public bool IsDragging => _dragging;

    /// <summary>The thumb was let go, with the value it landed on (a seek bar acts here, not on every step of the drag).</summary>
    public event Action<double>? Released;

    /// <summary>On a coloured widget: the thumb, fill and drop colour, the track's, and the number's.</summary>
    public Brush? JellyBrush { get; set; }
    public Brush? TrackBrush { get; set; }
    public Brush? LabelBrush { get; set; }

    public JellySlider()
    {
        Template = null;   // drawn here
        Focusable = false;   // like SoftSlider: a focused control keeps the island open (it thinks you are typing)
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Minimum = 0; Maximum = 1;
        _x = new S(R, Follow.K, Follow.C);
        _dx = new S(R, 260, 18);          // the drop's x lags behind: it leans
        _dy = new S(Rail / 2, Bubble.K, Bubble.C);
        _ds = new S(RestDrop, Bubble.K, Bubble.C);
        _ps = new S(1, 520, 16);          // hover / press size of the thumb
        SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5 && !_dragging) Place(snap: true); };
        Loaded += (_, _) => Place(snap: true);
        IsVisibleChanged += (_, _) => { if (!IsVisible) Stop(); };
    }

    // ------------------------------------------------------------------ geometry

    private double W => Math.Max(ActualWidth, 2 * R + 1);
    private double Span => Math.Max(W - 2 * R, 1);
    private double Ratio => Maximum > Minimum ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;
    private double ToX(double ratio) => R + ratio * Span;
    private double ToRatio(double x) => Math.Clamp((x - R) / Span, 0, 1);
    private static double Rubber(double x, double min, double max, double d = 18) =>
        x < min ? min - (1 - 1 / ((min - x) * .55 / d + 1)) * d :
        x > max ? max + (1 - 1 / ((x - max) * .55 / d + 1)) * d : x;

    private string Text(double value) => Format?.Invoke(value)
        ?? (Maximum - Minimum <= 1.0001 ? Math.Round((value - Minimum) / Math.Max(1e-9, Maximum - Minimum) * 100).ToString(CultureInfo.CurrentCulture)
                                        : Math.Round(value).ToString(CultureInfo.CurrentCulture));

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 160 : available.Width, Rail + (ShowScale ? ScaleHeight : 0));

    protected override Size ArrangeOverride(Size final) => final;

    // ------------------------------------------------------------------ value in, value out

    private void Place(bool snap)
    {
        double x = ToX(Ratio);
        // a value that creeps (a song playing: a fraction of a pixel a few times a second) just moves: no frames for it
        if (snap || !IsLoaded || !IsVisible || Math.Abs(x - _x.X) < 3 && !_ticking)
        {
            _x.X = _x.T = _dx.X = _dx.T = x; _x.V = _dx.V = 0;
            InvalidateVisual();
            return;
        }
        _x.Tune(Glide);
        _x.T = x;
        Start();
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        if (!_setting && !_dragging) Place(snap: false);
    }

    /// <summary>The value for a thumb position, snapped to ticks when the slider asks for it.</summary>
    private void SetFromX(double x)
    {
        double v = Minimum + ToRatio(x) * (Maximum - Minimum);
        if (IsSnapToTickEnabled && TickFrequency > 0) v = Minimum + Math.Round((v - Minimum) / TickFrequency) * TickFrequency;
        _setting = true;
        try { Value = Math.Clamp(v, Minimum, Maximum); }
        finally { _setting = false; }
    }

    // ------------------------------------------------------------------ pointer and keys

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); _hover = true; Sync(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); _hover = false; Sync(); }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (CanDrag?.Invoke() == false || !IsEnabled) return;
        e.Handled = true;
        CaptureMouse();
        Press(e.GetPosition(this).X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) Drag(e.GetPosition(this).X);
    }

    private void Press(double x)
    {
        _dragging = true;
        _x.Tune(Follow);
        _x.T = Rubber(x, R, W - R);
        SetFromX(_x.T);
        Sync();
    }

    private void Drag(double x)
    {
        _x.T = Rubber(x, R, W - R);
        SetFromX(_x.T);
        Start();
    }

    private void Let()
    {
        if (!_dragging) return;
        _dragging = false;
        // past an end: back on a softer, bouncier spring
        _x.Tune(Rebound);
        _x.T = Math.Clamp(_x.T, R, W - R);
        SetFromX(_x.T);
        Sync();
        Released?.Invoke(Value);
    }

    /// <summary>Tests (no mouse is moved): the same press, drag and release, at x in the slider's own pixels.</summary>
    internal void TestPress(double x) => Press(x);
    internal void TestDrag(double x) => Drag(x);
    internal void TestRelease() => Let();
    internal (double X, double Target, double Stretch, double DropRise, double DropScale, double Tilt, bool Moving) TestState =>
        (_x.X, _x.T, 1 + Math.Min(Math.Abs(_x.V) / 2400, .4), Rail / 2 - _dy.X, _ds.X, Math.Clamp((_dx.X - _x.X) * .9, -28, 28), _ticking);

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        e.Handled = true;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        Let();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        double? step = e.Key switch { Key.Right or Key.Up => 0.05, Key.Left or Key.Down => -0.05, Key.PageUp => 0.1, Key.PageDown => -0.1, _ => null };
        double? to = e.Key switch { Key.Home => 0, Key.End => 1, _ => null };
        if (step == null && to == null) { base.OnKeyDown(e); return; }
        e.Handled = true;
        double next = to ?? Math.Clamp(Math.Round(ToRatio(_x.T) * 20 + step!.Value * 20) / 20, 0, 1);
        _x.Tune(Glide);
        _x.T = ToX(next);
        SetFromX(_x.T);
        _lifted = true;   // the drop stays up while the keys move it: the value is its focus state
        Sync();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _lifted = false;
        Sync();
    }

    private void Sync()
    {
        bool up = _dragging || _lifted;
        _dy.T = up ? Rail / 2 + Rise : Rail / 2;
        _ds.T = up ? 1 : RestDrop;
        _ps.T = _dragging ? 1.16 : _hover ? 1.08 : 1;
        foreach (var s in new[] { _dy, _ds }) s.Tune(Bubble);
        _dx.Tune((260, 18));
        if (Motion.Reduced) _ps.Tune((520, 16));
        Start();
    }

    // ------------------------------------------------------------------ the frame loop (only while something moves)

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
        // motion.js: at most 1/30 s per frame, in 4 steps
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 1 / 30.0);
        _lastFrame = now;
        _dx.T = _x.X;
        for (int i = 0; i < 4; i++) foreach (var s in new[] { _x, _dx, _dy, _ds, _ps }) s.Step(dt / 4);
        InvalidateVisual();
        if (!_dragging && _x.Still(0.05) && _dx.Still(0.05) && _dy.Still(0.05) && _ds.Still(0.002) && _ps.Still(0.002))
        {
            foreach (var s in new[] { _x, _dx, _dy, _ds, _ps }) { s.X = s.T; s.V = 0; }
            Stop();
        }
    }

    // ------------------------------------------------------------------ drawing

    private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    protected override void OnRender(DrawingContext dc)
    {
        double w = W, cy = Rail / 2, x = _x.X;
        var jelly = JellyBrush ?? Res("Fg", Brushes.White);
        var track = TrackBrush ?? Res("TrackBg", new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, ActualHeight));   // the whole area takes the mouse

        // track, and the fill ending under the thumb's centre (gone by the time the thumb sits at 0)
        dc.DrawRoundedRectangle(track, null, new Rect(0, cy - 3, w, 6), 3, 3);
        double fillW = Math.Clamp(x, 0, w) * Math.Clamp((x - R) / R, 0, 1);
        if (fillW > 0.5) dc.DrawRoundedRectangle(jelly, null, new Rect(0, cy - 3, fillW, 6), 3, 3);

        // thumb: stretches along the track with its speed, keeping its area
        double st = 1 + Math.Min(Math.Abs(_x.V) / 2400, .4), ps = _ps.X;
        double rx = R * st * ps, ry = R * ps / st;
        var thumbC = new Point(x, cy);

        // drop: rises, leans the way it is dragged from; joined to the thumb by a liquid neck while close
        double tilt = Math.Clamp((_dx.X - x) * .9, -28, 28), dr = D * _ds.X;
        var dropC = new Point(_dx.X, _dy.X);
        if (dr > 0.5)
        {
            if (Goo.Neck(thumbC, R * ps, dropC, dr) is { } neck) dc.DrawGeometry(jelly, null, neck);
            dc.DrawEllipse(jelly, null, dropC, dr, dr);
        }
        dc.DrawEllipse(jelly, null, thumbC, rx, ry);

        // the number, in the drop, once it is mostly out
        double labelOpacity = Math.Clamp((_ds.X - .7) / .25, 0, 1);
        double shown = Minimum + ToRatio(x) * (Maximum - Minimum);
        if (labelOpacity > 0.01)
        {
            var ft = Formatted(Text(shown), 11, FontWeights.SemiBold, LabelBrush ?? Res("IslandBg", Brushes.Black));
            dc.PushOpacity(labelOpacity);
            dc.PushTransform(new RotateTransform(tilt, dropC.X, dropC.Y));
            dc.PushTransform(new ScaleTransform(_ds.X, _ds.X, dropC.X, dropC.Y));
            dc.DrawText(ft, new Point(dropC.X - ft.Width / 2, dropC.Y - ft.Height / 2));
            dc.Pop(); dc.Pop(); dc.Pop();
        }

        if (ShowScale) DrawScale(dc, w, ToRatio(x));
    }

    /// <summary>A mark every 5 %, a taller one every 25 % with its value under it; the ones the value passed light up.</summary>
    private void DrawScale(DrawingContext dc, double w, double ratio)
    {
        double top = Rail + 4;
        var dim = Res("TrackBg", Brushes.Gray);
        var lit = Res("Fg2", Brushes.LightGray);
        var litMajor = Res("Fg", Brushes.White);
        var muted = Res("Fg3", Brushes.Gray);
        for (int i = 0; i <= 20; i++)
        {
            double p = i / 20.0, mx = R + p * (w - 2 * R);
            bool major = i % 5 == 0, on = p <= ratio + 1e-9 && ratio > 0;
            var b = on ? (major ? litMajor : lit) : major ? muted : dim;
            dc.DrawRoundedRectangle(b, null, new Rect(mx - 0.75, top, 1.5, major ? 8 : 4), 0.75, 0.75);
            if (!major) continue;
            var ft = Formatted(Text(Minimum + p * (Maximum - Minimum)), 10, FontWeights.Medium, on ? lit : muted);
            dc.DrawText(ft, new Point(mx - ft.Width / 2, top + 12));
        }
    }

    private FormattedText Formatted(string text, double size, FontWeight weight, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}

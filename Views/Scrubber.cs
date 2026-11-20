using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// A design-tool scrubber (after SmoothUI's Scrubber by Eduardo Calvo): one 36 px bar with its name on the left
/// and its value on the right; the fill grows to the value, nine faint ticks mark the way, and a thin capsule at the
/// fill's edge wakes up (spring, bounce 0.1 over 0.25 s) while hovered or dragged. Grab it anywhere: it jumps
/// there and follows the pointer; arrows step, Home / End go to the ends. Away from the pointer the fill eases
/// over 150 ms. A RangeBase: Value, ValueChanged and LostMouseCapture work as on a Slider.
/// </summary>
public sealed class Scrubber : RangeBase
{
    private const double BarHeight = 36, Radius = 10, Ticks = 9, EaseSeconds = .15;
    // bounce 0.1 over 0.25 s as Motion turns it into a spring: k = (2π/0.25)², c = 2·0.9·√k
    private const double SpringK = 631.65, SpringC = 45.24;
    private static readonly CubicBezierEase EaseOut = new(0.23, 1, 0.32, 1);

    private double _shown, _from, _easeTime = -1;   // the fill's fraction on screen, easing toward the value
    private double _wake, _wakeV;                   // 0 asleep (faint, 70 %), 1 awake
    private bool _dragging, _ticking;
    private TimeSpan _last;

    /// <summary>The name on the left (already translated).</summary>
    public string Label { get; set { field = value; InvalidateVisual(); } } = "";

    /// <summary>The value as shown on the right ("110%", "нет").</summary>
    public Func<double, string> Format { get; set { field = value; InvalidateVisual(); } } = v => v.ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>Values snap to this (0 = no snapping).</summary>
    public double Step { get; set; }

    /// <summary>The fill's colour behind the value (defaults to the text colour, faint).</summary>
    public Brush? FillBrush { get; set; }

    static Scrubber()
    {
        FocusableProperty.OverrideMetadata(typeof(Scrubber), new FrameworkPropertyMetadata(true));
        MaximumProperty.OverrideMetadata(typeof(Scrubber), new FrameworkPropertyMetadata(100.0));
    }

    public Scrubber()
    {
        Height = BarHeight;
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        Loaded += (_, _) => { _shown = Fraction(Value); _wake = 0; InvalidateVisual(); };
        MouseEnter += (_, _) => Start();
        MouseLeave += (_, _) => Start();
    }

    private double Fraction(double v) => Maximum > Minimum ? Math.Clamp((v - Minimum) / (Maximum - Minimum), 0, 1) : 0;

    private double Snap(double v)
    {
        if (Step > 0) v = Math.Round((v - Minimum) / Step) * Step + Minimum;
        return Math.Clamp(v, Minimum, Maximum);
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        if (!IsLoaded) { _shown = Fraction(newValue); return; }
        if (_dragging || Motion.Reduced) { _shown = Fraction(newValue); _easeTime = -1; InvalidateVisual(); return; }
        _from = _shown; _easeTime = 0;
        Start();
    }

    private bool Awake => _dragging || IsMouseOver;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        Focus();
        _dragging = true;
        CaptureMouse();
        ValueFrom(e.GetPosition(this).X);
        Start();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) ValueFrom(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        e.Handled = true;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _dragging = false;
        Start();
        base.OnLostMouseCapture(e);
    }

    private void ValueFrom(double x)
    {
        double w = ActualWidth;
        if (w <= 0) return;
        Value = Snap(Minimum + Math.Clamp(x / w, 0, 1) * (Maximum - Minimum));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = Step > 0 ? Step : SmallChange;
        double? to = e.Key switch
        {
            Key.Right or Key.Up => Value + step,
            Key.Left or Key.Down => Value - step,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null,
        };
        if (to is not double v) { base.OnKeyDown(e); return; }
        e.Handled = true;
        Value = Snap(v);
    }

    private void Start()
    {
        if (Motion.Reduced) { _wake = Awake ? 1 : 0; _wakeV = 0; InvalidateVisual(); return; }
        if (_ticking) return;
        _ticking = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return;
        double dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _last).TotalSeconds, 1 / 30.0);
        _last = now;
        bool moving = false;
        if (_easeTime >= 0)
        {
            _easeTime += dt;
            double t = Math.Clamp(_easeTime / EaseSeconds, 0, 1);
            _shown = _from + (Fraction(Value) - _from) * EaseOut.Ease(t);
            if (t >= 1) _easeTime = -1; else moving = true;
        }
        double to = Awake ? 1 : 0;
        for (int i = 0; i < 4; i++) { _wakeV += (-SpringK * (_wake - to) - SpringC * _wakeV) * dt / 4; _wake += _wakeV * dt / 4; }
        if (Math.Abs(_wake - to) < .002 && Math.Abs(_wakeV) < .02) { _wake = to; _wakeV = 0; } else moving = true;
        InvalidateVisual();
        if (!moving) { _ticking = false; CompositionTarget.Rendering -= OnFrame; }
    }

    private static Color Alpha(Brush? b, double a)
    {
        var c = (b as SolidColorBrush)?.Color ?? Colors.White;
        return Color.FromArgb((byte)Math.Clamp(c.A * a, 0, 255), c.R, c.G, c.B);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0) return;
        var fg = TryFindResource("Fg") as Brush ?? Brushes.White;
        var bar = new Rect(0, 0, w, h);
        dc.PushClip(new RectangleGeometry(bar, Radius, Radius));
        dc.DrawRectangle(TryFindResource("TrackBg") as Brush ?? new SolidColorBrush(Alpha(fg, .12)), null, bar);

        double x = w * Math.Clamp(_shown, 0, 1);
        if (x > 0) dc.DrawRoundedRectangle(FillBrush ?? new SolidColorBrush(Alpha(fg, .14)), null, new Rect(0, 0, x, h), Radius, Radius);

        var tick = new SolidColorBrush(Alpha(fg, .25));
        for (int i = 1; i <= Ticks; i++)
            dc.DrawRoundedRectangle(tick, null, new Rect(Math.Round(w * i / (Ticks + 1)) - .5, h / 2 - 3, 1, 6), .5, .5);

        // the capsule at the fill's edge: faint and 70 % asleep, 4 × 22 awake
        double wake = Math.Clamp(_wake, -.1, 1.1), scale = .7 + .3 * wake, cw = 4 * scale, ch = 22 * scale;
        double cx = Math.Clamp(x, 6, w - 6);
        dc.DrawRoundedRectangle(new SolidColorBrush(Alpha(fg, .9 * Math.Clamp(.15 + .65 * wake, 0, 1))), null,
            new Rect(cx - cw / 2, h / 2 - ch / 2, cw, ch), cw / 2, cw / 2);
        dc.Pop();

        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        var value = new FormattedText(Format(Value), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 13, fg, dip);
        var name = new FormattedText(Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, 13, fg, dip)
        {
            MaxTextWidth = Math.Max(1, w - 12 - 10 - value.Width - 12),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        dc.DrawText(name, new Point(12, (h - name.Height) / 2));
        dc.DrawText(value, new Point(w - 10 - value.Width, (h - value.Height) / 2));
    }

    /// <summary>Tests: the fill on screen, how awake the capsule is, dragging.</summary>
    internal (double Shown, double Wake, bool Dragging, bool Moving) TestState => (_shown, _wake, _dragging, _ticking);
    internal void TestPress(double x) { _dragging = true; ValueFrom(x); Start(); }
    internal void TestDrag(double x) => ValueFrom(x);
    internal void TestRelease() { _dragging = false; Start(); }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// A row (or column) of radio buttons with one rubber pill under the chosen one (after designbookmark's Rubber
/// dock): the pill's two edges are separate springs — the leading one stiff, the trailing one lazy — so moving
/// to another button stretches it like rubber, keeping its area; the chosen icon pops. The pill can be dragged
/// along the buttons, with rubber past the ends, and lands on the nearest. Drawn under the buttons as the
/// panel's own background, so it moves, fades and scales with the panel; the buttons' own highlight is hidden.
/// </summary>
public sealed class RubberStack : StackPanel
{
    // tokens.css, Rubber dock (stiffness, damping; mass 1)
    private static readonly (double K, double C) Lead = (640, 38), Trail = (150, 16), DragLead = (1100, 56), DragTrail = (320, 26), Even = (600, 44), Pop = (520, 16);

    private sealed class S(double x, double k, double c)
    {
        public double X = x, V, T = x, K = k, C = c;
        public void Tune((double K, double C) s) { K = s.K; C = Motion.Reduced ? 2 * Math.Sqrt(s.K) : s.C; }
        public void Step(double dt) { double a = -K * (X - T) - C * V; V += a * dt; X += V * dt; }
        public bool Still => Math.Abs(X - T) < 0.05 && Math.Abs(V) < 0.5;
    }

    private readonly S _a = new(0, Lead.K, Lead.C), _b = new(0, Lead.K, Lead.C);   // the pill's start and end along the row
    private readonly Dictionary<ToggleButton, (S Spring, ScaleTransform Scale)> _pops = [];
    private ToggleButton? _chosen;
    private Rect _target;
    private bool _placed, _ticking, _pressed, _dragging;
    private double _pressAt, _lastAt, _dir;
    private TimeSpan _lastFrame;
    private readonly SolidColorBrush _fill = new();
    private readonly LinearGradientBrush _rim = new() { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
    private readonly ScaleTransform _scale = new();

    /// <summary>Corner radius of the pill (it never gets rounder than half its thickness).</summary>
    public double PillRadius { get; set; } = 16;
    /// <summary>The thin light rim the island's tabs have.</summary>
    public bool Rim { get; set; }

    private bool Horizontal => Orientation == Orientation.Horizontal;

    public RubberStack()
    {
        _rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), 0));
        _rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0.5));
        _rim.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), 1));
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, e) => { if (e.OriginalSource is ToggleButton t && t.Parent == this) Choose(t); }));
        Loaded += (_, _) => { foreach (var t in Children.OfType<ToggleButton>()) Adopt(t); _chosen ??= Children.OfType<ToggleButton>().FirstOrDefault(t => t.IsChecked == true); Retarget(snap: true); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) Stop(); };
    }

    protected override void OnVisualChildrenChanged(DependencyObject? added, DependencyObject? removed)
    {
        base.OnVisualChildrenChanged(added, removed);
        if (added is ToggleButton t) { Adopt(t); if (t.IsChecked == true) _chosen = t; }
        if (removed is ToggleButton r) _pops.Remove(r);
    }

    /// <summary>A button joins: its own highlight goes (the pill is the highlight), its icon gets a pop.</summary>
    private void Adopt(ToggleButton t)
    {
        void Hook()
        {
            t.ApplyTemplate();
            if (t.Template?.FindName("Sel", t) is UIElement sel) sel.Visibility = Visibility.Collapsed;
            if (_pops.ContainsKey(t)) return;
            var icon = FindIcon(t);
            if (icon == null) return;
            var scale = new ScaleTransform(1, 1);
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = scale;
            _pops[t] = (new S(1, Pop.K, Pop.C), scale);
        }
        if (t.IsLoaded) Hook(); else t.Loaded += (_, _) => Hook();
    }

    private static TextBlock? FindIcon(DependencyObject d)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is TextBlock { Text.Length: 1 } tb) return tb;   // the icon glyph (the label is a ContentPresenter)
            if (FindIcon(c) is { } deeper) return deeper;
        }
        return null;
    }

    private void Choose(ToggleButton t)
    {
        _chosen = t;
        // the pill fades and scales with the chosen button (the island's tabs melt into dots and back)
        BindingOperations.SetBinding(_fill, Brush.OpacityProperty, new Binding(nameof(Opacity)) { Source = t });
        BindingOperations.SetBinding(_rim, Brush.OpacityProperty, new Binding(nameof(Opacity)) { Source = t });
        BindingOperations.SetBinding(_scale, ScaleTransform.ScaleXProperty, new Binding("RenderTransform.ScaleX") { Source = t, FallbackValue = 1.0 });
        BindingOperations.SetBinding(_scale, ScaleTransform.ScaleYProperty, new Binding("RenderTransform.ScaleY") { Source = t, FallbackValue = 1.0 });
        if (_pops.TryGetValue(t, out var pop) && !Motion.Reduced) pop.Spring.V -= 3.2;
        Retarget(snap: !_placed || !IsVisible);
    }

    // ------------------------------------------------------------------ where the pill goes

    /// <summary>
    /// Where a button is laid out, without its own render transform: the island's tabs scale from 0.55 to 1 when
    /// they melt in from the dots, and a place taken mid-animation put the pill a quarter of a button off (19 px
    /// right and down) until the next layout change. The pill scales with the button by itself.
    /// </summary>
    private Rect BoundsOf(FrameworkElement el) =>
        !el.IsArrangeValid || el.ActualWidth <= 0 ? Rect.Empty
        : new Rect(VisualTreeHelper.GetParent(el) == this ? (Point)VisualTreeHelper.GetOffset(el) : el.TranslatePoint(new Point(0, 0), this), new Size(el.ActualWidth, el.ActualHeight));

    private double Center(Rect r) => Horizontal ? r.X + r.Width / 2 : r.Y + r.Height / 2;
    private double Length(Rect r) => Horizontal ? r.Width : r.Height;

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = base.ArrangeOverride(arrangeSize);
        // the chosen tab shows its name and grows: the pill follows its new size
        Dispatcher.BeginInvoke(() => { if (!_dragging) Retarget(snap: !_placed); }, System.Windows.Threading.DispatcherPriority.Render);
        return size;
    }

    private void Retarget(bool snap)
    {
        if (_chosen == null) return;
        var r = BoundsOf(_chosen);
        if (r.IsEmpty) return;
        double from = Horizontal ? r.X : r.Y, to = from + Length(r);
        bool changed = Math.Abs(_a.T - from) > 0.5 || Math.Abs(_b.T - to) > 0.5 || _target != r;
        _target = r;
        if (snap || !_placed)
        {
            _a.X = _a.T = from; _b.X = _b.T = to; _a.V = _b.V = 0;
            _placed = true;
            InvalidateVisual();
            return;
        }
        if (!changed) return;
        MoveTo(from, to, Math.Sign((from + to) / 2 - (_a.X + _b.X) / 2), drag: false);
    }

    /// <summary>Moving forward, the far edge leads (stiff) and the near one trails (lazy): it stretches. Back the other way round.</summary>
    private void MoveTo(double from, double to, double dir, bool drag)
    {
        var (lead, trail) = drag ? (DragLead, DragTrail) : (Lead, Trail);
        if (dir == 0) { _a.Tune(Even); _b.Tune(Even); }
        else if (dir > 0) { _a.Tune(trail); _b.Tune(lead); }
        else { _a.Tune(lead); _b.Tune(trail); }
        _a.T = from; _b.T = to;
        Start();
    }

    // ------------------------------------------------------------------ dragging the pill along the buttons

    private double Along(MouseEventArgs e) { var p = e.GetPosition(this); return Horizontal ? p.X : p.Y; }

    private static double Rubber(double x, double min, double max, double d = 46) =>
        x < min ? min - (1 - 1 / ((min - x) * .55 / d + 1)) * d :
        x > max ? max + (1 - 1 / ((x - max) * .55 / d + 1)) * d : x;

    private List<(ToggleButton Button, double Center)> Stops() =>
        Children.OfType<ToggleButton>().Where(t => t.IsVisible).Select(t => (t, BoundsOf(t))).Where(x => !x.Item2.IsEmpty)
                .Select(x => (x.t, Center(x.Item2))).OrderBy(x => x.Item2).ToList();

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        _pressed = true; _dragging = false;
        _pressAt = _lastAt = Along(e); _dir = 0;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed || _chosen == null) return;
        double at = Along(e);
        if (!_dragging)
        {
            if (Math.Abs(at - _pressAt) <= 4) return;
            _dragging = true;
            CaptureMouse();   // the button under the press gets no click: the drag decides
        }
        if (Math.Abs(at - _lastAt) > .5) _dir = Math.Sign(at - _lastAt);
        _lastAt = at;
        var stops = Stops();
        if (stops.Count == 0) return;
        double c = Rubber(at, stops[0].Center, stops[^1].Center), half = Length(_target) / 2;
        MoveTo(c - half, c + half, _dir, drag: true);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        _pressed = false;
        if (!_dragging) return;
        e.Handled = true;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_dragging) return;
        _dragging = false; _pressed = false;
        var stops = Stops();
        if (stops.Count == 0) return;
        // lands on the nearest button, a little further the way it was thrown
        double c = (_a.T + _b.T) / 2, step = stops.Count > 1 ? (stops[^1].Center - stops[0].Center) / (stops.Count - 1) : 1;
        var pick = stops.OrderBy(s => Math.Abs(s.Center - (c + _dir * .18 * step))).First().Button;
        if (pick.IsChecked == true) Retarget(snap: false); else pick.IsChecked = true;
    }

    /// <summary>Tests (no mouse is moved): the pill's two edges and how fast they move.</summary>
    internal (double From, double To, double FromSpeed, double ToSpeed, bool Moving) TestState => (_a.X, _b.X, _a.V, _b.V, _ticking);

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
        for (int i = 0; i < 4; i++)
        {
            _a.Step(dt / 4); _b.Step(dt / 4);
            foreach (var (s, _) in _pops.Values) s.Step(dt / 4);
        }
        foreach (var (s, scale) in _pops.Values) scale.ScaleX = scale.ScaleY = s.X;
        InvalidateVisual();
        if (!_dragging && _a.Still && _b.Still && _pops.Values.All(p => Math.Abs(p.Spring.X - 1) < 0.002 && Math.Abs(p.Spring.V) < 0.02))
        {
            _a.X = _a.T; _b.X = _b.T; _a.V = _b.V = 0;
            foreach (var (s, scale) in _pops.Values) { s.X = 1; s.V = 0; scale.ScaleX = scale.ScaleY = 1; }
            Stop();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_chosen == null || _target.IsEmpty) return;
        double a = Math.Min(_a.X, _b.X), b = Math.Max(_a.X, _b.X);
        double len = Math.Max(b - a, 24), rest = Length(_target);
        // area preserving: stretched longer, it gets thinner (and fatter when squeezed)
        // (a full-width menu row only slims a little: thinned by a third it no longer sits under its icons)
        double thick = (Horizontal ? _target.Height : _target.Width) * Math.Clamp(1 / Math.Sqrt(len / Math.Max(1, rest)), Horizontal ? .66 : .92, Horizontal ? 1.1 : 1.04);
        double mid = Horizontal ? _target.Y + _target.Height / 2 : _target.X + _target.Width / 2;
        var rect = Horizontal ? new Rect(a, mid - thick / 2, len, thick) : new Rect(mid - thick / 2, a, thick, len);
        double radius = Math.Min(PillRadius, thick / 2);
        _fill.Color = (TryFindResource("ChipBg") as SolidColorBrush)?.Color ?? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF);
        _scale.CenterX = _target.X + _target.Width / 2; _scale.CenterY = _target.Y + _target.Height / 2;
        dc.PushTransform(_scale);
        dc.DrawRoundedRectangle(_fill, Rim ? new Pen(_rim, 1) : null, rect, radius, radius);
        dc.Pop();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// A plan as a list of steps (after SmoothUI's AI task list by Eduardo Calvo): a light streak runs under the step
/// in progress, a finished step's box turns green, its check draws itself in and the row dims and settles 1 px
/// lower; "3/5" counts the finished ones. Same timings as the original; nothing moves while no step runs.
/// </summary>
public sealed class TaskListView : StackPanel
{
    public enum State { Pending, Running, Done }
    public sealed record Step(string Key, string Text, State State);

    // ai-task-list: a spring of bounce 0.1 over 0.25 s (k = (2π/0.25)², c = 2·0.9·√k), the box 0.2 s,
    // the check 200 ms on cubic-bezier(.23,1,.32,1), the streak 1.4 s on cubic-bezier(.645,.045,.355,1)
    private const double SpringK = 631.65, SpringC = 45.24, BoxSeconds = .2, DrawSeconds = .2, DoneOpacity = .65;
    private static readonly CubicBezierEase DrawEase = new(0.23, 1, 0.32, 1), StreakEase = new(0.645, 0.045, 0.355, 1);
    private static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58);

    private sealed class Row
    {
        public required string Key;
        public required Grid Root;
        public required TaskBox Box;
        public required TextBlock Label;
        public required Rectangle Streak;
        public readonly TranslateTransform Move = new();
        public State State;
        public double Y, V, O = 1, OV;   // the spring: 1 px drop and the dimming
    }

    private readonly TextBlock _title = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _count = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 1) };
    private readonly StackPanel _rows = new();
    private readonly List<Row> _list = [];
    private bool _ticking;
    private TimeSpan _last;

    public TaskListView()
    {
        _count.SetResourceReference(TextBlock.ForegroundProperty, "Fg3");
        System.Windows.Documents.Typography.SetNumeralAlignment(_count, FontNumeralAlignment.Tabular);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(_count, Dock.Right);
        head.Children.Add(_count);
        head.Children.Add(_title);
        Children.Add(head);
        Children.Add(_rows);
        IsVisibleChanged += (_, _) => { foreach (var r in _list) Streak(r); };
    }

    public string Title { get => _title.Text; set => _title.Text = value; }

    /// <summary>The plan as it is now: rows are kept by key, so a step that changes state animates in place.</summary>
    public void Set(IReadOnlyList<Step> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var row = _list.FirstOrDefault(r => r.Key == s.Key);
            if (row == null)
            {
                row = MakeRow(s);
                _list.Insert(Math.Min(i, _list.Count), row);
                _rows.Children.Insert(Math.Min(i, _rows.Children.Count), row.Root);
                row.State = s.State;
                row.O = s.State == State.Done ? DoneOpacity : 1;
                row.Y = s.State == State.Done && !Motion.Reduced ? 1 : 0;
                row.Box.Snap(s.State == State.Done);
            }
            else
            {
                int at = _list.IndexOf(row);
                if (at != i) { _list.RemoveAt(at); _list.Insert(i, row); _rows.Children.Remove(row.Root); _rows.Children.Insert(i, row.Root); }
                if (row.State != s.State) { row.State = s.State; row.Box.Go(s.State == State.Done); Start(); }
            }
            row.Label.Text = s.Text;
            Streak(row);
        }
        foreach (var gone in _list.Where(r => steps.All(s => s.Key != r.Key)).ToList())
        {
            _list.Remove(gone);
            _rows.Children.Remove(gone.Root);
        }
        _count.Text = $"{steps.Count(s => s.State == State.Done)}/{steps.Count}";
        Apply();
    }

    private Row MakeRow(Step s)
    {
        var box = new TaskBox { Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        var label = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        // 1 px, a soft highlight half the row wide sweeping across it
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), RelativeTransform = new TranslateTransform() };
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        brush.GradientStops.Add(new GradientStop(Colors.White, .25));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, .5));
        var streak = new Rectangle { Height = 1, Fill = brush, Opacity = .5, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        var line = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(box, Dock.Left);
        line.Children.Add(box);
        line.Children.Add(label);
        var row = new Row { Key = s.Key, Root = new Grid { Children = { line, streak } }, Box = box, Label = label, Streak = streak };
        row.Root.RenderTransform = row.Move;
        return row;
    }

    private void Streak(Row r)
    {
        bool on = r.State == State.Running && IsVisible && !Motion.Reduced;
        if (on == (r.Streak.Visibility == Visibility.Visible)) return;
        r.Streak.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var move = (TranslateTransform)((LinearGradientBrush)r.Streak.Fill).RelativeTransform;
        if (!on) { move.BeginAnimation(TranslateTransform.XProperty, null); return; }
        ((LinearGradientBrush)r.Streak.Fill).GradientStops[1].Color = (r.Label.Foreground as SolidColorBrush)?.Color ?? Colors.White;
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.4)) { EasingFunction = StreakEase, RepeatBehavior = RepeatBehavior.Forever });
    }

    private void Apply()
    {
        foreach (var r in _list)
        {
            r.Root.Opacity = Math.Clamp(r.O, 0, 1);
            r.Move.Y = r.Y;
        }
    }

    private void Start()
    {
        if (Motion.Reduced)
        {
            foreach (var r in _list) { r.O = r.State == State.Done ? DoneOpacity : 1; r.Y = 0; r.V = r.OV = 0; r.Box.Finish(); }
            Apply();
            return;
        }
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
        foreach (var r in _list)
        {
            double ty = r.State == State.Done ? 1 : 0, to = r.State == State.Done ? DoneOpacity : 1;
            for (int i = 0; i < 4; i++)
            {
                double h = dt / 4;
                r.V += (-SpringK * (r.Y - ty) - SpringC * r.V) * h; r.Y += r.V * h;
                r.OV += (-SpringK * (r.O - to) - SpringC * r.OV) * h; r.O += r.OV * h;
            }
            if (Math.Abs(r.Y - ty) < .01 && Math.Abs(r.V) < .1 && Math.Abs(r.O - to) < .002 && Math.Abs(r.OV) < .02) { r.Y = ty; r.O = to; r.V = r.OV = 0; }
            else moving = true;
            moving |= r.Box.Step(dt);
        }
        Apply();
        if (!moving) { _ticking = false; CompositionTarget.Rendering -= OnFrame; }
    }

    /// <summary>Tests: each row's text, state, dimming, drop, box tint and how much of the check is drawn.</summary>
    internal (string Count, (string Text, State State, double Opacity, double Y, double Tint, double Check, bool Streak)[] Rows) TestState =>
        (_count.Text, _list.Select(r => (r.Label.Text, r.State, r.Root.Opacity, r.Y, r.Box.Tint, r.Box.Check, r.Streak.Visibility == Visibility.Visible)).ToArray());

    /// <summary>The 14 px box: a rounded square that tints green and draws its check in when the step is done.</summary>
    private sealed class TaskBox : FrameworkElement
    {
        private double _tint, _tintFrom, _tintTime = -1, _check = 1, _drawTime = -1;
        private bool _done;
        public double Tint => _tint;
        public double Check => _done ? _check : 0;

        public TaskBox() { Width = Height = 14; }

        public void Snap(bool done) { _done = done; _tint = done ? 1 : 0; _check = 1; InvalidateVisual(); }

        public void Go(bool done)
        {
            if (done == _done) return;
            _done = done;
            _tintFrom = _tint; _tintTime = 0;
            // the check draws in; going back it simply goes (the original unmounts it)
            _check = done ? 0 : 1; _drawTime = done ? 0 : -1;
            InvalidateVisual();
        }

        public void Finish() { _tint = _done ? 1 : 0; _tintTime = -1; _check = 1; _drawTime = -1; InvalidateVisual(); }

        public bool Step(double dt)
        {
            bool moving = false, was = _tintTime >= 0 || _drawTime >= 0;
            if (_tintTime >= 0)
            {
                _tintTime += dt;
                double t = Math.Clamp(_tintTime / BoxSeconds, 0, 1), to = _done ? 1 : 0;
                _tint = _tintFrom + (to - _tintFrom) * CssEase.Ease(t);
                if (t >= 1) _tintTime = -1; else moving = true;
            }
            if (_drawTime >= 0)
            {
                _drawTime += dt;
                double t = Math.Clamp(_drawTime / DrawSeconds, 0, 1);
                _check = DrawEase.Ease(t);
                if (t >= 1) _drawTime = -1; else moving = true;
            }
            if (was) InvalidateVisual();
            return moving;
        }

        // CSS "ease", what the box's own 0.2 s transition uses
        private static readonly CubicBezierEase CssEase = new(0.25, 0.1, 0.25, 1);

        private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        protected override void OnRender(DrawingContext dc)
        {
            var fg = (TryFindResource("Fg3") as SolidColorBrush)?.Color ?? Colors.Gray;
            var stroke = new Pen(new SolidColorBrush(Mix(fg, Green, _tint)), 1.4);
            var fill = new SolidColorBrush(Color.FromArgb((byte)(0.12 * 255 * _tint), Green.R, Green.G, Green.B));
            dc.DrawRoundedRectangle(fill, stroke, new Rect(1, 1, 12, 12), 3.5, 3.5);
            if (!_done || _check <= 0) return;
            // M 3.5 7.5 L 6 10 L 10.5 4.5, drawn up to _check of its length
            Point a = new(3.5, 7.5), b = new(6, 10), c = new(10.5, 4.5);
            double ab = (b - a).Length, bc = (c - b).Length, len = (ab + bc) * Math.Clamp(_check, 0, 1);
            var pen = new Pen(new SolidColorBrush(Green), 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(a, false, false);
                if (len <= ab) ctx.LineTo(a + (b - a) * (len / ab), true, true);
                else { ctx.LineTo(b, true, true); ctx.LineTo(b + (c - b) * ((len - ab) / bc), true, true); }
            }
            dc.DrawGeometry(null, pen, g);
        }
    }
}

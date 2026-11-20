using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// The song's cover as a record (after SmoothUI's "Music toggle", MIT): the disc turns while the music plays,
/// a ring around it shows how far the song is, and the hole in the middle is the play / pause button.
/// Laid out at 96 × 96 like the original and scaled to whatever size it is given.
/// </summary>
public sealed class MusicRecordView : Grid
{
    private const double Design = 96;
    private static readonly Duration Swap = TimeSpan.FromMilliseconds(250);

    private readonly Border _disc;
    private readonly RotateTransform _spin = new();
    private readonly Ring _ring = new();
    private readonly (TextBlock Glyph, ScaleTransform Scale, RotateTransform Turn) _play, _pause;
    private bool _playing, _spinning;

    /// <summary>Tests: how far the record has turned, in degrees.</summary>
    public double Angle => _spin.Angle;

    /// <summary>The hole in the middle was clicked.</summary>
    public event Action? Toggled;

    /// <summary>Behind the play / pause glyph (the island's own colour in the island, dark on a widget).</summary>
    public Brush CenterBrush { set => _center.Background = value; }
    public Brush GlyphBrush { set { _play.Glyph.Foreground = value; _pause.Glyph.Foreground = value; } }
    public Brush TrackBrush { set => _ring.Track = value; }
    public Brush ProgressBrush { set => _ring.Fill = value; }

    private readonly Border _center;

    public MusicRecordView()
    {
        var face = new Grid { Width = Design, Height = Design };

        face.Children.Add(_ring);

        // the record: 80 % of the size, the cover in it, turning about its middle
        _disc = new Border
        {
            Width = Design * 0.8, Height = Design * 0.8, CornerRadius = new CornerRadius(Design * 0.4), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _spin,
        };
        _disc.SetResourceReference(Border.BorderBrushProperty, "TileStroke");
        _disc.Clip = new EllipseGeometry(new Point(Design * 0.4, Design * 0.4), Design * 0.4, Design * 0.4);
        face.Children.Add(_disc);

        // the hole is the button: 24 % of the size, with a soft shadow
        _play = Glyph("", fromTurn: -45);
        _pause = Glyph("", fromTurn: 45);
        var glyphs = new Grid { Children = { _play.Glyph, _pause.Glyph } };
        _center = new Border
        {
            Width = Design * 0.24, Height = Design * 0.24, CornerRadius = new CornerRadius(Design * 0.12), Child = glyphs, Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Direction = 270, Opacity = 0.35 },
        };
        _center.SetResourceReference(Border.BackgroundProperty, "IslandBg");
        _center.MouseLeftButtonUp += (_, e) => { e.Handled = true; Toggled?.Invoke(); };
        face.Children.Add(_center);

        Children.Add(new Viewbox { Child = face, Stretch = Stretch.Uniform });
        System.Windows.Automation.AutomationProperties.SetName(this, L.T("Пауза или воспроизведение"));
        Show(_pause, false, instant: true);
        Show(_play, true, instant: true);
        SetCover(null);
        IsVisibleChanged += (_, _) => Spin();
    }

    private (TextBlock, ScaleTransform, RotateTransform) Glyph(string text, double fromTurn)
    {
        var scale = new ScaleTransform(1, 1);
        var turn = new RotateTransform(fromTurn);
        var t = new TextBlock
        {
            Text = text, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new TransformGroup { Children = { scale, turn } },
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        return (t, scale, turn);
    }

    /// <summary>The song's cover, or null: a soft accent-coloured record.</summary>
    public void SetCover(ImageSource? art)
    {
        if (art != null) { _disc.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill }; return; }
        var accent = (TryFindResource("Accent") as SolidColorBrush)?.Color ?? Color.FromRgb(0x30, 0xD1, 0x58);
        _disc.Background = new LinearGradientBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B), Color.FromArgb(0x1A, accent.R, accent.G, accent.B), 45);
    }

    /// <summary>How far the song is, 0…1: the ring follows in a quarter of a second.</summary>
    public void SetProgress(double progress) => _ring.MoveTo(Math.Clamp(progress, 0, 1));

    public void SetPlaying(bool playing)
    {
        if (playing == _playing) return;
        _playing = playing;
        // one glyph leaves before the other comes (the original's mode="wait"): pause turns in from 45°, play from -45°
        var (gone, comes) = playing ? (_play, _pause) : (_pause, _play);
        if (Motion.Reduced) { Show(gone, false, instant: true); Show(comes, true, instant: true); }
        else Show(gone, false, instant: false, then: () => { if (_playing == playing) Show(comes, true, instant: false); });
        Spin();
    }

    private static void Show((TextBlock Glyph, ScaleTransform Scale, RotateTransform Turn) g, bool shown, bool instant, Action? then = null)
    {
        // leaving: play turns to 45°, pause to -45° — the mirror of how each comes in
        double turn = shown ? 0 : g.Glyph.Text == "" ? 45 : -45;
        if (shown && !instant) g.Turn.Angle = g.Glyph.Text == "" ? -45 : 45;
        double scale = shown ? 1 : 0.6, opacity = shown ? 1 : 0;
        if (instant)
        {
            g.Glyph.BeginAnimation(OpacityProperty, null); g.Glyph.Opacity = opacity;
            g.Turn.BeginAnimation(RotateTransform.AngleProperty, null); g.Turn.Angle = turn;
            g.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); g.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            g.Scale.ScaleX = g.Scale.ScaleY = scale;
            then?.Invoke();
            return;
        }
        // spring, a quarter of a second, a little bounce (the original's { duration: 0.25, bounce: 0.1 })
        DoubleAnimation A(double to) => new(to, Swap) { EasingFunction = new SpringEase { DampingRatio = 0.9 } };
        var fade = A(opacity);
        if (then != null) fade.Completed += (_, _) => then();
        g.Glyph.BeginAnimation(OpacityProperty, fade);
        g.Turn.BeginAnimation(RotateTransform.AngleProperty, A(turn));
        g.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, A(scale));
        g.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, A(scale));
    }

    /// <summary>
    /// One turn every 15 s, steady, while the music plays and the record can be seen; on pause it stops where it
    /// is. At 30 frames a second: the turn is slow, and the island must not draw more than it needs to.
    /// </summary>
    private void Spin()
    {
        bool want = _playing && IsVisible && !Motion.Reduced;
        if (want == _spinning) return;
        _spinning = want;
        double now = _spin.Angle % 360;
        if (!want)
        {
            _spin.BeginAnimation(RotateTransform.AngleProperty, null);
            _spin.Angle = now;
            return;
        }
        var turn = new DoubleAnimation(now, now + 360, TimeSpan.FromSeconds(15)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(turn, 30);
        _spin.BeginAnimation(RotateTransform.AngleProperty, turn);
    }

    /// <summary>The thin ring: a faint full circle, and the part of the song played so far from 12 o'clock.</summary>
    private sealed class Ring : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Ring),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public Brush? Track { get; set; }
        public Brush? Fill { get; set; }
        private double _target = -1;

        public Ring()
        {
            Width = Height = Design;
            IsHitTestVisible = false;
        }

        public void MoveTo(double v)
        {
            if (Math.Abs(v - _target) < 0.0005) return;
            _target = v;
            if (Motion.Reduced) { BeginAnimation(ValueProperty, null); Value = v; return; }
            BeginAnimation(ValueProperty, new DoubleAnimation(v, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicBezierEase(0.645, 0.045, 0.355, 1) });
        }

        protected override void OnRender(DrawingContext dc)
        {
            const double r = Design * 0.46, thick = Design * 0.04;
            var c = new Point(Design / 2, Design / 2);
            var track = Track ?? TryFindResource("Fg") as Brush ?? Brushes.White;
            dc.PushOpacity(Track == null ? 0.1 : 1);
            dc.DrawEllipse(null, new Pen(track, thick), c, r, r);
            dc.Pop();
            double v = Math.Clamp(Value, 0, 1);
            if (v <= 0.001) return;
            var pen = new Pen(Fill ?? TryFindResource("Accent") as Brush ?? Brushes.White, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (v >= 0.999) { dc.DrawEllipse(null, pen, c, r, r); return; }
            double a = v * 2 * Math.PI;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X, c.Y - r), false, false);
                ctx.ArcTo(new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)), new Size(r, r), 0, v > 0.5, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }
}

/// <summary>
/// Thin bars that pulse while the music plays (after SmoothUI's "Music toggle"): each bar has its own height,
/// speed and start, from a fixed pseudo-random seed, so the pattern is the same every time; on pause they rest low.
/// </summary>
public sealed class MusicBarsView : StackPanel
{
    private readonly List<(ScaleTransform Scale, double Min, double Max, double Seconds, double Delay)> _bars = [];
    private bool _playing, _moving;

    public MusicBarsView() : this(24) { }

    public MusicBarsView(int count)
    {
        Orientation = Orientation.Horizontal;
        Height = 20;
        IsHitTestVisible = false;
        for (int i = 0; i < count; i++)
        {
            double h = Hash(i), d = Hash(i + 0.5), p = Hash(i + 0.25);
            double min = 0.2 + 0.15 * h, max = 1 - 0.3 * (1 - h), seconds = 0.6 + 0.5 * d;
            var scale = new ScaleTransform(1, min);
            var bar = new Border
            {
                Width = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 0, i < count - 1 ? 2 : 0, 0),
                RenderTransformOrigin = new Point(0.5, 1), RenderTransform = scale, Opacity = 0.7,
            };
            bar.SetResourceReference(Border.BackgroundProperty, "Fg");
            Children.Add(bar);
            _bars.Add((scale, min, max, seconds, p * seconds));
        }
        IsVisibleChanged += (_, _) => Move();
    }

    /// <summary>Tests: the bars' heights added up (it changes while they pulse).</summary>
    public double Level => _bars.Sum(b => b.Scale.ScaleY);

    /// <summary>A white widget card: the bars take this colour instead of the theme's text colour.</summary>
    public Brush BarBrush { set { foreach (Border b in Children) b.Background = value; } }

    public void SetPlaying(bool playing)
    {
        if (playing == _playing) return;
        _playing = playing;
        Move();
    }

    private static double Hash(double seed)
    {
        double x = Math.Sin(seed * 12.9898) * 43758.5453;
        return x - Math.Floor(x);
    }

    private void Move()
    {
        bool want = _playing && IsVisible && !Motion.Reduced;
        if (want == _moving) return;
        _moving = want;
        foreach (var (scale, min, max, seconds, delay) in _bars)
        {
            if (!want)
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);   // rests at once, like the original
                scale.ScaleY = min;
                continue;
            }
            // min → max → min in one period, again and again
            var pulse = new DoubleAnimation(min, max, TimeSpan.FromSeconds(seconds / 2))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay),
                EasingFunction = new CubicBezierEase(0.645, 0.045, 0.355, 1),
            };
            Timeline.SetDesiredFrameRate(pulse, 30);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
    }
}

/// <summary>A CSS cubic-bezier(x1, y1, x2, y2) curve for WPF animations.</summary>
public sealed class CubicBezierEase : EasingFunctionBase
{
    private readonly double _x1, _y1, _x2, _y2;

    public CubicBezierEase() : this(0.25, 0.1, 0.25, 1) { }

    public CubicBezierEase(double x1, double y1, double x2, double y2)
    {
        _x1 = x1; _y1 = y1; _x2 = x2; _y2 = y2;
        EasingMode = EasingMode.EaseIn;   // EaseInCore is the whole curve
    }

    private static double Bezier(double t, double a, double b) => 3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;

    protected override double EaseInCore(double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        // the t whose x is the given time, by bisection (the curve's x grows with t)
        double lo = 0, hi = 1, t = x;
        for (int i = 0; i < 24; i++)
        {
            t = (lo + hi) / 2;
            if (Bezier(t, _x1, _x2) < x) lo = t; else hi = t;
        }
        return Bezier(t, _y1, _y2);
    }

    protected override Freezable CreateInstanceCore() => new CubicBezierEase(_x1, _y1, _x2, _y2);
}

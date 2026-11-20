using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// A line whose letters gain weight near the pointer (after designbookmark's Weight wave): each letter's weight
/// is a spring pulled toward a bell curve around the pointer, so a swell of bold rolls through the line as the
/// mouse moves over it; with no mouse, while <see cref="Playing"/>, the swell sweeps back and forth by itself.
/// WPF can't move a font's weight smoothly (it picks the nearest named face), so a letter between two of the
/// font's faces (light, semilight, regular, semibold, bold, black) is the two neighbours blended, its width
/// following — neighbours are close in shape, so the blend doesn't ghost. Frames only while something moves.
/// </summary>
public sealed class WeightWaveText : FrameworkElement
{
    // wave.js: letters 260 / 22, the swell's place 150 / 22; light 300, bold 820; the swell 13 % of the line wide
    private const double Light = 300, Bold = 820, Spread = .13, Sweep = 4.6;

    private sealed class S(double x, double k, double c)
    {
        public double X = x, V, T = x;
        public double K = k, C = Motion.Reduced ? 2 * Math.Sqrt(k) : c;
        public void Step(double dt) { double a = -K * (X - T) - C * V; V += a * dt; X += V * dt; }
        public bool Still => Math.Abs(X - T) < 0.5 && Math.Abs(V) < 2;
    }

    private string _text = "";
    // the faces a weight passes through on the way from light to bold; _faces[face][letter]
    private static readonly double[] Steps = [300, 350, 400, 600, 700, 820];
    private FormattedText[][] _faces = [];
    private FormattedText[] _light => _faces.Length > 0 ? _faces[0] : [];
    private S[] _weights = [];
    private readonly S _focus = new(-.2, 150, 22);
    private bool _hover, _playing, _ticking;
    private double _clock;
    private TimeSpan _lastFrame, _lastDraw;
    private FormattedText? _ellipsis;

    public string Text
    {
        get => _text;
        set
        {
            if (value == _text) return;
            _text = value ?? "";
            Build();
            InvalidateMeasure();
            InvalidateVisual();
            Start();
        }
    }

    public double FontSize { get; set; } = 16;

    /// <summary>The song plays: with no mouse on the line, the swell sweeps by itself.</summary>
    public bool Playing
    {
        get => _playing;
        set { if (value == _playing) return; _playing = value; _clock = 0; Start(); }
    }

    public WeightWaveText()
    {
        IsVisibleChanged += (_, _) => { if (IsVisible) Start(); else Stop(); };
        // the island's font can change (Settings > Layout): the letters are measured again
        DependencyPropertyDescriptor.FromProperty(TextElement.FontFamilyProperty, typeof(WeightWaveText))?.AddValueChanged(this, (_, _) => { Build(); InvalidateMeasure(); });
    }

    private void Build()
    {
        var family = TextElement.GetFontFamily(this);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        FormattedText F(string s, FontWeight w) => new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, w, FontStretches.Normal), FontSize, Brushes.White, dpi);   // coloured when drawn
        var chars = System.Globalization.StringInfo.GetTextElementEnumerator(_text);
        var list = new List<string>();
        while (chars.MoveNext()) list.Add((string)chars.Current);
        _faces = Steps.Select(w => list.Select(c => F(c, FontWeight.FromOpenTypeWeight((int)w))).ToArray()).ToArray();
        var old = _weights;
        _weights = list.Select((_, i) => i < old.Length ? old[i] : new S(Light, 260, 22)).ToArray();
        _ellipsis = F("…", FontWeight.FromOpenTypeWeight((int)Light));
    }

    private double Track => -0.03 * FontSize;   // the original's letter-spacing: -.03em

    private double Blend(int i) => Math.Clamp((_weights[i].X - Light) / (Bold - Light), 0, 1);

    /// <summary>The two faces around a letter's weight, and how far it is from the first to the second.</summary>
    private (int Face, double Toward) Between(int i)
    {
        double w = Math.Clamp(_weights[i].X, Steps[0], Steps[^1]);
        int k = 0;
        while (k < Steps.Length - 2 && w > Steps[k + 1]) k++;
        return (k, (w - Steps[k]) / (Steps[k + 1] - Steps[k]));
    }

    private double CellWidth(int i)
    {
        var (k, f) = Between(i);
        double a = _faces[k][i].WidthIncludingTrailingWhitespace, b = _faces[k + 1][i].WidthIncludingTrailingWhitespace;
        return Math.Max(0, a + (b - a) * f + Track);
    }

    protected override Size MeasureOverride(Size available)
    {
        if (_light.Length == 0) Build();
        double w = 0, h = _light.Length > 0 ? _light.Max(f => f.Height) : FontSize * 1.3;
        for (int i = 0; i < _light.Length; i++) w += _faces[^1][i].WidthIncludingTrailingWhitespace + Track;   // room for any letter at its boldest
        return new Size(double.IsInfinity(available.Width) ? w : Math.Min(w, available.Width), h);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _hover = true;
        _focus.T = e.GetPosition(this).X / Math.Max(1, LineWidth());
        Start();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _clock = 0;
        Start();
    }

    private double LineWidth()
    {
        double w = 0;
        for (int i = 0; i < _light.Length; i++) w += CellWidth(i);
        return Math.Max(w, 1);
    }

    /// <summary>Promo films: a pointer over the title at x (its own DIPs), or gone (null).</summary>
    internal void TestPointer(double? x)
    {
        _hover = x != null;
        if (x is double at) _focus.T = at / Math.Max(1, LineWidth()); else _clock = 0;
        Start();
    }

    /// <summary>Tests: the boldest letter (index), how bold it is (0…1), whether frames run.</summary>
    internal (int Boldest, double MaxBlend, bool Moving) TestState
    {
        get
        {
            int best = 0;
            for (int i = 1; i < _weights.Length; i++) if (_weights[i].X > _weights[best].X) best = i;
            return (best, _weights.Length > 0 ? Blend(best) : 0, _ticking);
        }
    }

    // ------------------------------------------------------------------ frames

    private void Start()
    {
        if (_ticking || !IsVisible || _light.Length == 0) { InvalidateVisual(); return; }
        _ticking = true;
        _lastFrame = _lastDraw = TimeSpan.Zero;
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
        bool sweeping = !_hover && _playing && !Motion.Reduced;
        if (sweeping)
        {
            _clock += dt;
            _focus.T = -.06 + 1.12 * (.5 - .5 * Math.Cos(_clock * Math.PI * 2 / Sweep));
        }
        else if (!_hover) _focus.T = -.2;   // nobody points, nothing plays: the line rests light
        for (int s = 0; s < 4; s++) _focus.Step(dt / 4);

        // each letter's weight is pulled toward the bell curve around the swell
        double total = LineWidth(), x = 0;
        for (int i = 0; i < _weights.Length; i++)
        {
            double c = (x + CellWidth(i) / 2) / total, d = (c - _focus.X) / Spread;
            x += CellWidth(i);
            _weights[i].T = Light + (Bold - Light) * Math.Exp(-d * d);
            for (int s = 0; s < 4; s++) _weights[i].Step(dt / 4);
        }
        // 30 frames a second are plenty for a swell this slow
        if (_lastDraw == TimeSpan.Zero || (now - _lastDraw).TotalSeconds >= 1 / 31.0) { _lastDraw = now; InvalidateVisual(); }
        if (!sweeping && !_hover && _focus.Still && _weights.All(w => w.Still)) { InvalidateVisual(); Stop(); }
    }

    // ------------------------------------------------------------------ drawing

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));   // the mouse is felt between the letters too
        if (_light.Length == 0) return;
        var fg = (TryFindResource("Fg") as SolidColorBrush)?.Color ?? Colors.White;
        double avail = ActualWidth, h = ActualHeight, x = 0;
        double full = LineWidth();
        double ell = (_ellipsis?.WidthIncludingTrailingWhitespace ?? 0);
        bool cut = full > avail + 0.5;
        for (int i = 0; i < _light.Length; i++)
        {
            double w = CellWidth(i);
            if (cut && x + w > avail - ell)
            {
                if (_ellipsis != null) Draw(dc, _ellipsis, null, 0, 0, x, h, fg, ell);
                break;
            }
            var (k, f) = Between(i);
            Draw(dc, _faces[k][i], _faces[k + 1][i], f, Blend(i), x, h, fg, w);
            x += w;
        }
    }

    /// <summary>
    /// One letter: its two neighbouring faces on top of each other, blended, centred in its cell. Lighter letters
    /// are also fainter: the original goes from .42 to 1; a song's name must stay readable at rest, so from .7.
    /// </summary>
    private static void Draw(DrawingContext dc, FormattedText light, FormattedText? bold, double t, double weight, double x, double h, Color fg, double cell)
    {
        double alpha = .7 + .3 * weight;
        void Face(FormattedText f, double share)
        {
            if (share <= 0.01) return;
            f.SetForegroundBrush(new SolidColorBrush(Color.FromArgb((byte)(fg.A * alpha * share), fg.R, fg.G, fg.B)));
            dc.DrawText(f, new Point(x + (cell - f.WidthIncludingTrailingWhitespace) / 2, (h - f.Height) / 2));
        }
        Face(light, bold == null ? 1 : 1 - t);
        if (bold != null) Face(bold, t);
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// A copy button that says it worked (after designbookmark's Copy button): the copy icon shrinks and turns away
/// while a check pops in, the word rolls up from "Copy" to "Copied" and the button's width springs to fit; after
/// a moment it rolls back. A Button underneath: it keeps the style it is given (pill, ghost, accent, icon).
/// Springs from copy.js; frames only while it moves.
/// </summary>
public sealed class CopyButton : Button
{
    private sealed class S(double x, double k, double c)
    {
        public double X = x, V, T = x;
        public readonly double K = k, C = Motion.Reduced ? 2 * Math.Sqrt(k) : c;
        public void Step(double dt) { double a = -K * (X - T) - C * V; V += a * dt; X += V * dt; }
        public bool Still(double eps) => Math.Abs(X - T) < eps && Math.Abs(V) < eps * 10;
    }

    // copy.js: the roll 260 / 20, the icon 420 / 18, the width 300 / 24
    private readonly S _roll = new(0, 260, 20), _icon = new(0, 420, 18), _width = new(0, 300, 24);
    private readonly TextBlock _copyIcon, _okIcon, _word, _done;
    private readonly ScaleTransform _copyScale = new(), _okScale = new();
    private readonly RotateTransform _copyTurn = new();
    private readonly TranslateTransform _rollMove = new();
    private readonly Canvas _rollBox;
    private bool _ticking, _on;
    private TimeSpan _lastFrame;
    private DateTime _backAt;

    /// <summary>
    /// What to copy, asked on click (null: nothing to copy, nothing happens). Left null, the click is the
    /// caller's: it copies itself and calls <see cref="Done"/>.
    /// </summary>
    public Func<string?>? Text { get; set; }

    /// <summary>After a successful copy (logging, closing a view).</summary>
    public event Action? Copied;

    /// <summary>The check and the second word's colour (green; on an accent button its own text colour reads better).</summary>
    public Brush? DoneBrush { get; set; }

    public CopyButton() : this(null, null) { }

    /// <summary>The word at rest; set after construction too (XAML). Null or empty: icon only.</summary>
    public string? Label
    {
        get => _word.Text;
        set
        {
            _word.Text = value ?? "";
            var row = (StackPanel)Content;
            bool has = row.Children.Contains(_rollBox);
            if (!string.IsNullOrEmpty(value) && !has) row.Children.Add(_rollBox);
            if (string.IsNullOrEmpty(value) && has) row.Children.Remove(_rollBox);
            if (IsLoaded) Fit(snap: true);
        }
    }

    /// <param name="label">The word at rest ("Копировать"); null: icon only.</param>
    public CopyButton(string? label, string? doneLabel = null)
    {
        var glyphs = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        _copyIcon = new TextBlock { Text = "", FontFamily = glyphs, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                                    RenderTransformOrigin = new Point(.5, .5), RenderTransform = new TransformGroup { Children = { _copyScale, _copyTurn } } };
        _okIcon = new TextBlock { Text = "", FontFamily = glyphs, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                                  RenderTransformOrigin = new Point(.5, .5), RenderTransform = _okScale, Opacity = 0 };
        var icon = new Grid { Width = 16, Height = 16, Children = { _copyIcon, _okIcon } };
        _word = new TextBlock { Text = label ?? "" };
        _done = new TextBlock { Text = doneLabel ?? L.T("Скопировано") };
        var stack = new StackPanel { RenderTransform = _rollMove, Children = { _word, _done } };
        _rollBox = new Canvas { ClipToBounds = true, Margin = new Thickness(7, 0, 0, 0), Children = { stack } };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon } };
        if (label != null) row.Children.Add(_rollBox);
        Content = row;
        Loaded += (_, _) => { Paint(); Fit(snap: true); };
        Click += (_, _) => OnCopyClick();
    }

    private double LineHeight => Math.Ceiling(FontSize * 1.34);

    private double WordWidth(TextBlock t)
    {
        var f = new FormattedText(t.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return Math.Ceiling(f.WidthIncludingTrailingWhitespace);
    }

    private void Fit(bool snap)
    {
        _rollBox.Height = LineHeight;
        _word.Height = _done.Height = LineHeight;
        double w = WordWidth(_on ? _done : _word);
        _width.T = w;
        if (snap) { _width.X = w; _width.V = 0; _rollBox.Width = w; }
    }

    private void Paint()
    {
        var done = DoneBrush ?? new SolidColorBrush(Color.FromRgb(0x34, 0xD1, 0x7A));
        _okIcon.Foreground = done;
        _done.Foreground = done;
    }

    private void OnCopyClick()
    {
        if (Text == null) return;
        var text = Text();
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); }
        catch (Exception e) { Log.Write("copy button: " + e.Message); return; }
        Done();
    }

    /// <summary>It worked: the icon becomes a check and the word rolls to "Copied" for 1.8 s.</summary>
    public void Done()
    {
        _on = true;
        _roll.T = 1; _icon.T = 1;
        Fit(snap: false);
        _backAt = DateTime.UtcNow.AddSeconds(1.8);
        Start();
        Copied?.Invoke();
    }

    /// <summary>Tests: the roll, the icon swap and the label width.</summary>
    internal (double Roll, double Icon, double Width, bool Moving) TestState => (_roll.X, _icon.X, _width.X, _ticking);

    private void Start()
    {
        if (_ticking) return;
        _ticking = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 1 / 30.0);
        _lastFrame = now;
        if (_on && DateTime.UtcNow >= _backAt)
        {
            _on = false; _roll.T = 0; _icon.T = 0;
            Fit(snap: false);
        }
        for (int s = 0; s < 4; s++) { _roll.Step(dt / 4); _icon.Step(dt / 4); _width.Step(dt / 4); }

        _rollBox.Width = Math.Max(0, _width.X);
        _rollMove.Y = -LineHeight * _roll.X;
        double i = Math.Clamp(_icon.X, -.2, 1.2), i01 = Math.Clamp(i, 0, 1);
        _copyIcon.Opacity = Math.Clamp(1 - i * 1.6, 0, 1);
        _copyScale.ScaleX = _copyScale.ScaleY = 1 - .5 * i01;
        _copyTurn.Angle = -40 * i01;
        _okIcon.Opacity = Math.Clamp(i * 1.6 - .4, 0, 1);
        _okScale.ScaleX = _okScale.ScaleY = Math.Max(0, .4 + .6 * i);

        if (!_on && _roll.Still(0.002) && _icon.Still(0.002) && _width.Still(0.2))
        {
            _ticking = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }
}

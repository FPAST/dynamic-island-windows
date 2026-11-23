using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>The interactive widgets: calculator, stopwatch, water, to-do list, countdown, volume, apps, photos, moon, month, processes, breathing.</summary>
public partial class MainWindow
{
    // ------------------------------------------------------------------ small parts

    /// <summary>The rim of a glass control: lit from above, a softer glint along the bottom.</summary>
    private static readonly Brush GlassRim = Frz(new LinearGradientBrush
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
        GradientStops = { new GradientStop(Color.FromArgb(0x78, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0x14, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0x38, 255, 255, 255), 1) },
    });

    private static readonly Brush GlassSheen = Frz(new LinearGradientBrush
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
        GradientStops = { new GradientStop(Color.FromArgb(0x26, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.55) },
    });

    /// <summary>
    /// A glass button (Apple's interactive glass): a rim and a sheen; under the pointer it lights up, and pressed
    /// it swells a little and glows instead of sinking. The click only outside edit mode (unless told otherwise).
    /// A transparent background makes it a plain button inside a glass group. name: what screen readers say.
    /// </summary>
    private Border WKey(object label, Brush? bg, Brush? fg, Action click, double w, double h, double radius = -1, string? name = null, bool evenWhenEditing = false)
    {
        var content = label is string s ? W(s, h > 40 ? 19 : 14, FontWeights.Medium, fg) : (UIElement)label;
        if (content is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
        double r = radius >= 0 ? radius : double.IsNaN(w) ? h / 2 : Math.Min(w, h) / 2;
        bool glass = !(bg is SolidColorBrush sb && sb.Color.A == 0);
        var glow = new Border { CornerRadius = new CornerRadius(r), Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
        var g = new Grid();
        if (glass) g.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = GlassSheen, IsHitTestVisible = false });
        g.Children.Add(glow);
        g.Children.Add(content);
        var k = new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(r), Child = g, Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
            BorderThickness = new Thickness(glass ? 1 : 0), BorderBrush = GlassRim,
        };
        if (bg == null) k.SetResourceReference(Border.BackgroundProperty, "WFill"); else k.Background = bg;
        k.Tag = content;   // the label itself (the child is the glass layers around it)
        if (name != null) { System.Windows.Automation.AutomationProperties.SetName(k, name); if (label is not string) k.ToolTip = name; }
        bool down = false;
        bool Off() => _widgetsEditing && !evenWhenEditing;
        void To(double scale, double light, int ms)
        {
            if (Motion.Reduced) { ((ScaleTransform)k.RenderTransform).ScaleX = ((ScaleTransform)k.RenderTransform).ScaleY = 1; glow.Opacity = light; return; }
            var a = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut } };
            ((ScaleTransform)k.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, a);
            ((ScaleTransform)k.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, a);
            glow.BeginAnimation(OpacityProperty, new DoubleAnimation(light, TimeSpan.FromMilliseconds(ms / 2 + 60)));
        }
        k.MouseEnter += (_, _) => { if (!Off() && !down) To(1, glass ? 0.06 : 0.08, 200); };
        k.MouseLeave += (_, _) => { down = false; To(1, 0, 260); };
        k.MouseLeftButtonDown += (_, e) => { if (Off()) return; e.Handled = true; down = true; To(1.07, 0.16, 180); };
        k.MouseLeftButtonUp += (_, e) => { if (Off()) return; e.Handled = true; if (!down) return; down = false; To(1, 0.06, 320); click(); };
        return k;
    }

    /// <summary>Controls that belong together share one glass capsule (like grouped toolbar items).</summary>
    private static Border GlassGroup(UIElement content, double radius)
    {
        var b = new Border { CornerRadius = new CornerRadius(radius), Padding = new Thickness(6), BorderThickness = new Thickness(1), BorderBrush = GlassRim, Child = content };
        b.SetResourceReference(Border.BackgroundProperty, "WFill");
        return b;
    }

    /// <summary>
    /// A slider's knob: a small dot at the end of the fill, shown under the pointer; while it's dragged it turns
    /// into a pill of glass (as Apple's sliders do). Makes the track tall enough to grab.
    /// </summary>
    private static Action<bool> GlassKnob(Grid track, Border fill, double barHeight)
    {
        track.Height = 22;
        foreach (var c in track.Children.OfType<Border>()) { c.Height = barHeight; c.VerticalAlignment = VerticalAlignment.Center; }
        var knob = new Border
        {
            Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Opacity = 0, BorderBrush = GlassRim,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.3 },
        };
        track.Children.Add(knob);
        void Place() => knob.Margin = new Thickness(Math.Max(0, fill.ActualWidth - knob.ActualWidth / 2), 0, 0, 0);
        fill.SizeChanged += (_, _) => Place();
        knob.SizeChanged += (_, _) => Place();
        bool active = false;
        track.MouseEnter += (_, _) => knob.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        track.MouseLeave += (_, _) => { if (!active) knob.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))); };
        return on =>
        {
            active = on;
            var d = TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 260);
            var ease = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
            knob.BeginAnimation(WidthProperty, new DoubleAnimation(on ? 32 : 12, d) { EasingFunction = ease });
            knob.BeginAnimation(HeightProperty, new DoubleAnimation(on ? 22 : 12, d) { EasingFunction = ease });
            knob.CornerRadius = new CornerRadius(on ? 11 : 6);
            knob.Background = on ? new SolidColorBrush(Color.FromArgb(0x4C, 0xFF, 0xFF, 0xFF)) : Brushes.White;
            knob.BorderThickness = new Thickness(on ? 1.2 : 0);
            knob.Opacity = 1;
            if (!on && !track.IsMouseOver) knob.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
        };
    }

    /// <summary>A text field on the glass, with a placeholder.</summary>
    private (Border Box, TextBox Text) Field(string placeholder, double size = 13.5)
    {
        var tb = new TextBox
        {
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = size, Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        tb.Style = null;   // the plain system box, see-through on the glass
        tb.SetResourceReference(ForegroundProperty, "WFg");
        tb.SetResourceReference(TextBoxBase.CaretBrushProperty, "WFg");
        var ph = W(placeholder, size, null, Dim); ph.IsHitTestVisible = false; ph.VerticalAlignment = VerticalAlignment.Center; ph.Margin = new Thickness(2, 0, 0, 0);
        tb.TextChanged += (_, _) => ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var g = new Grid(); g.Children.Add(ph); g.Children.Add(tb);
        var box = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 8), Child = g };
        box.SetResourceReference(Border.BackgroundProperty, "WFill");
        tb.Loaded += (_, _) => Dispatcher.BeginInvoke(() => { if (tb.IsVisible && Window.GetWindow(tb) is { IsActive: true }) tb.Focus(); }, DispatcherPriority.Input);
        return (box, tb);
    }

    private static StackPanel Row(params UIElement[] items)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items) s.Children.Add(i);
        return s;
    }

    // ------------------------------------------------------------------ calculator

    private Action CalcWidget(Grid body)
    {
        var sep = Loc.Culture.NumberFormat.NumberDecimalSeparator;
        double acc = 0; char? pending = null; bool typing = false; string entry = "0";
        var expr = W("", 13, null, Dim); expr.HorizontalAlignment = HorizontalAlignment.Right;
        var display = W("0", 46, FontWeights.Light, null, big: true); display.HorizontalAlignment = HorizontalAlignment.Right; display.TextTrimming = TextTrimming.None;
        var fit = new Viewbox { Child = display, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Right, Height = 52 };
        var top = new StackPanel { Margin = new Thickness(0, 0, 4, 6) }; top.Children.Add(expr); top.Children.Add(fit);

        double Val() => double.TryParse(entry.Replace("\u202F", "").Replace(sep, "."), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        string Fmt(double x) => double.IsNaN(x) || double.IsInfinity(x) ? L.T("Ошибка")
            : (Math.Abs(x) >= 1e12 || (Math.Abs(x) < 1e-9 && x != 0) ? x.ToString("0.#####E+0", CultureInfo.InvariantCulture) : Math.Round(x, 10).ToString("#,0.##########", CultureInfo.InvariantCulture))
              .Replace(",", "\u202F").Replace(".", sep);
        static double Apply(double a, char op, double b) => op switch { '+' => a + b, '−' => a - b, '×' => a * b, '÷' => b == 0 ? double.NaN : a / b, _ => b };

        TextBlock? clearLabel = null;
        var opKeys = new Dictionary<char, Border>();
        var orange = Solid(0xFF, 0x9F, 0x0A);
        void Show()
        {
            display.Text = typing ? (entry.Length > 0 && entry != "-" ? entry : "0") : entry;
            if (clearLabel != null) clearLabel.Text = typing && entry != "0" ? "C" : "AC";
            expr.Text = pending != null ? Fmt(acc) + " " + pending : "";
            foreach (var (op, key) in opKeys)
            {
                bool sel = pending == op && !typing;
                key.Background = sel ? Brushes.White : orange;
                if (key.Tag is TextBlock t) t.Foreground = sel ? orange : Brushes.White;
            }
        }
        void Digit(string d)
        {
            if (!typing) { entry = "0"; typing = true; }
            if (d == sep) { if (!entry.Contains(sep)) entry += sep; }
            else if (entry.Replace(sep, "").Replace("-", "").Length < 12) entry = entry == "0" ? d : entry == "-0" ? "-" + d : entry + d;
            Show();
        }
        void Op(char o)
        {
            double v = Val();
            if (pending != null && typing) acc = Apply(acc, pending.Value, v);
            else if (pending == null) acc = v;
            pending = o; typing = false; entry = Fmt(acc);
            Show();
        }
        void Equal()
        {
            if (pending == null) return;
            acc = Apply(acc, pending.Value, Val());
            entry = Fmt(acc); pending = null; typing = false;
            Show();
        }
        void Clear()
        {
            if (typing && entry != "0") { entry = "0"; }
            else { acc = 0; pending = null; entry = "0"; typing = false; }
            Show();
        }

        var gray = Solid(0xA5, 0xA5, 0xA5, 0xD8); var dark = Solid(0x1C, 0x1C, 0x1E);
        var keys = new UniformGrid { Columns = 4, Rows = 5 };
        const double kw = 68, kh = 44;
        void Add(Border b) { b.Margin = new Thickness(3); keys.Children.Add(b); }
        var c = WKey("AC", gray, dark, Clear, kw, kh); clearLabel = (TextBlock)c.Tag; Add(c);
        Add(WKey("±", gray, dark, () => { entry = entry.StartsWith('-') ? entry[1..] : "-" + entry; typing = true; Show(); }, kw, kh));
        Add(WKey("%", gray, dark, () => { entry = Fmt(Val() / 100); typing = true; Show(); }, kw, kh));
        Border OpKey(char o) { var k = WKey(o.ToString(), orange, Brushes.White, () => Op(o), kw, kh); opKeys[o] = k; return k; }
        Add(OpKey('÷'));
        foreach (var (d1, d2, d3, o) in new[] { ("7", "8", "9", '×'), ("4", "5", "6", '−'), ("1", "2", "3", '+') })
        {
            foreach (var d in new[] { d1, d2, d3 }) Add(WKey(d, null, null, () => Digit(d), kw, kh));
            Add(OpKey(o));
        }
        Add(WKey("0", null, null, () => Digit("0"), kw, kh));
        Add(WKey(sep, null, null, () => Digit(sep), kw, kh));
        Add(WKey(Glyph("", 15), null, null, () => { if (typing && entry.Length > 1) entry = entry[..^1]; else entry = "0"; if (entry == "-") entry = "0"; Show(); }, kw, kh, name: L.T("Стереть")));
        Add(WKey("=", orange, Brushes.White, Equal, kw, kh));

        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top); dock.Children.Add(keys);
        body.Children.Add(dock);
        Show();
        return () => { };
    }

    // ------------------------------------------------------------------ stopwatch

    private static DateTime? _swStarted;
    private static TimeSpan _swBefore;
    private static readonly List<TimeSpan> _swLaps = [];
    private static TimeSpan SwElapsed => _swBefore + (_swStarted is { } s ? DateTime.Now - s : TimeSpan.Zero);

    private Action StopwatchWidget(Grid body, bool big = false)
    {
        var accent = Solid(0xFF, 0x9F, 0x0A);
        var time = W("00:00,0", big ? 52 : 30, FontWeights.Light, null, big: true); time.TextTrimming = TextTrimming.None;
        var fit = new Viewbox { Child = time, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left };
        var green = Solid(0x30, 0xD1, 0x58, 0x50); var red = Solid(0xFF, 0x45, 0x3A, 0x50);
        double bs = big ? 64 : 44;
        var startLabel = W("", big ? 15 : 12, FontWeights.SemiBold);
        var lapLabel = W("", big ? 15 : 12, FontWeights.SemiBold);
        Action after = () => { };
        var start = WKey(startLabel, green, null, () =>
        {
            if (_swStarted is { } s) { _swBefore += DateTime.Now - s; _swStarted = null; }
            else _swStarted = DateTime.Now;
            after();
        }, bs, bs);
        var lap = WKey(lapLabel, null, null, () =>
        {
            if (_swStarted != null) _swLaps.Insert(0, SwElapsed);
            else { _swBefore = TimeSpan.Zero; _swLaps.Clear(); }
            after();
        }, bs, bs);
        var buttons = new DockPanel { VerticalAlignment = VerticalAlignment.Bottom, LastChildFill = false };
        DockPanel.SetDock(start, Dock.Right);
        buttons.Children.Add(lap); buttons.Children.Add(start);
        var laps = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var top = new StackPanel();
        top.Children.Add(Caption("\uE916", L.T("Секундомер"), accent));
        top.Children.Add(fit);
        if (big) { top.Children.Add(buttons); buttons.Margin = new Thickness(0, 10, 0, 0); top.Children.Add(laps); body.Children.Add(top); }
        else { body.Children.Add(top); body.Children.Add(buttons); }

        static string F(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds / 100}";
        int shownLaps = -1;
        void Refresh()
        {
            time.Text = F(SwElapsed);
            bool running = _swStarted != null;
            startLabel.Text = running ? L.T("Стоп") : L.T("Старт");
            startLabel.Foreground = running ? Solid(0xFF, 0x6B, 0x60) : Solid(0x30, 0xD1, 0x58);
            start.Background = running ? red : green;
            lapLabel.Text = running || SwElapsed == TimeSpan.Zero ? L.T("Круг") : L.T("Сброс");
            if (big && shownLaps != _swLaps.Count)
            {
                shownLaps = _swLaps.Count;
                laps.Children.Clear();
                for (int i = 0; i < Math.Min(6, _swLaps.Count); i++)
                {
                    var lapTime = _swLaps[i] - (i + 1 < _swLaps.Count ? _swLaps[i + 1] : TimeSpan.Zero);
                    var r = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                    var v = W(F(lapTime), 14, FontWeights.SemiBold); DockPanel.SetDock(v, Dock.Right);
                    r.Children.Add(v); r.Children.Add(W(L.F("Круг {0}", _swLaps.Count - i), 14, null, Dim));
                    laps.Children.Add(r);
                }
            }
        }
        // ten times a second while it runs
        var fast = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        fast.Tick += (_, _) => Measure("widget stopwatch", () => { if (PresentationSource.FromVisual(body) == null) { fast.Stop(); return; } Refresh(); if (_swStarted == null) fast.Stop(); });
        after = () => { Refresh(); if (_swStarted != null) fast.Start(); };
        return () => { Refresh(); if (_swStarted != null && !fast.IsEnabled) fast.Start(); };
    }

    // ------------------------------------------------------------------ water

    private int WaterToday
    {
        get
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            if (_settings.WaterDay != today) { _settings.WaterDay = today; _settings.WaterCount = Settings.ReadOnly ? 5 : 0; }
            return _settings.WaterCount;
        }
    }

    private Action WaterWidget(Grid body, Border wash)
    {
        var blue = Color.FromRgb(0x32, 0xAD, 0xE6);
        // the water itself: rises with each glass, its surface a slow wave
        var water = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Height = 0, ClipToBounds = false };
        water.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0x90, 0x5A, 0xC8, 0xFA), Color.FromArgb(0xC0, 0x0A, 0x84, 0xFF), 90), Margin = new Thickness(0, 8, 0, 0) });
        var wave = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(0x90, 0x5A, 0xC8, 0xFA)), Height = 10, VerticalAlignment = VerticalAlignment.Top, Stretch = Stretch.None };
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 10), true, true);
            for (int x = 0; x <= 1400; x += 6) c.LineTo(new Point(x, 4 + Math.Sin(x / 40.0) * 3.5), true, false);
            c.LineTo(new Point(1400, 10), true, false);
        }
        g.Freeze();
        wave.Data = g;
        var slide = new TranslateTransform();
        wave.RenderTransform = slide;
        var waveAnim = new DoubleAnimation(0, -Math.PI * 80, TimeSpan.FromSeconds(3.2)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(waveAnim, 24);   // a slow wave doesn't need 60 frames a second
        if (!Motion.Reduced) slide.BeginAnimation(TranslateTransform.XProperty, waveAnim);
        water.Children.Add(wave);
        wash.Child = water;
        wash.ClipToBounds = true;

        var count = W("", 40, FontWeights.Light, null, big: true); count.Margin = new Thickness(-1, -4, 0, 0);
        var of = W("", 11.5, FontWeights.SemiBold, Dim);
        var top = new StackPanel(); top.Children.Add(Caption("\uE95E", L.T("Вода"), new SolidColorBrush(blue))); top.Children.Add(count); top.Children.Add(of);
        var plus = WKey(Glyph("\uE710", 14), Solid(0xFF, 0xFF, 0xFF, 0xE6), null, () => { _settings.WaterCount = WaterToday + 1; _settings.Save(); }, 40, 40, name: L.T("Добавить стакан"));
        ((TextBlock)plus.Tag).Foreground = new SolidColorBrush(blue);
        var minus = WKey(Glyph("\uE738", 12), null, null, () => { _settings.WaterCount = Math.Max(0, WaterToday - 1); _settings.Save(); }, 30, 30, name: L.T("Убрать стакан"));
        var buttons = Row(minus, plus); buttons.HorizontalAlignment = HorizontalAlignment.Right; buttons.VerticalAlignment = VerticalAlignment.Bottom;
        minus.Margin = new Thickness(0, 0, 6, 5); minus.VerticalAlignment = VerticalAlignment.Bottom;
        body.Children.Add(top); body.Children.Add(buttons);
        int shown = -1;
        double Level() => Math.Min(1, (double)WaterToday / Math.Max(1, _settings.WaterGoal));
        // the card got its real size (or grew when opened): the water follows without a splash
        wash.SizeChanged += (_, _) => { water.BeginAnimation(HeightProperty, null); water.Height = wash.ActualHeight * Level(); };
        return () =>
        {
            int n = WaterToday, goal = Math.Max(1, _settings.WaterGoal);
            count.Text = n.ToString();
            of.Text = n >= goal ? L.T("норма выполнена") : L.F("из {0} стаканов", goal);
            if (n == shown) return;
            shown = n;
            if (wash.ActualHeight <= 0) return;
            double h = wash.ActualHeight * Level();
            // a height can't go below zero: an easing that overshoots downwards would throw
            water.BeginAnimation(HeightProperty, new DoubleAnimation(Math.Max(0, h), TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        };
    }

    // ------------------------------------------------------------------ to-do list

    private static readonly List<WidgetTodo> DemoTodos =
    [
        new() { Text = L.T("Отправить отчёт") }, new() { Text = L.T("Купить подарок") },
        new() { Text = L.T("Записаться к врачу"), Done = true }, new() { Text = L.T("Полить цветы") },
    ];

    private List<WidgetTodo> Todos => Settings.ReadOnly && _settings.WidgetTodos.Count == 0 ? DemoTodos : _settings.WidgetTodos;

    private Action TodoWidget(Grid body, bool compact)
    {
        var accent = Acc(0x30, 0xD1, 0x58);
        var head = new DockPanel();
        bool bigList = _buildBig;
        var left = W("", compact ? 26 : 30, FontWeights.Light, accent, big: true); left.Margin = new Thickness(0, -8, compact ? 0 : 40, 0);   // opened: room for the close button
        DockPanel.SetDock(left, Dock.Right);
        head.Children.Add(left); head.Children.Add(Caption("\uE762", L.T("Список дел"), accent));
        var list = new StackPanel { Margin = new Thickness(0, compact ? 6 : 10, 0, 0) };
        var stack = new StackPanel(); stack.Children.Add(head);
        TextBox? input = null;
        if (!compact)
        {
            var (box, tb) = Field(L.T("Новое дело — Enter, чтобы добавить"));
            box.Margin = new Thickness(0, 10, 0, 0);
            tb.KeyDown += (_, e) =>
            {
                if (e.Key != System.Windows.Input.Key.Enter || tb.Text.Trim().Length == 0) return;
                _settings.WidgetTodos.Insert(0, new WidgetTodo { Text = tb.Text.Trim() });
                _settings.Save(); tb.Text = ""; e.Handled = true;
                TickWidgets();
            };
            input = tb;
            stack.Children.Add(box);
        }
        stack.Children.Add(list);
        body.Children.Add(stack);
        if (!compact)
        {
            var clear = Pill(L.T("Убрать сделанные"), () => { _settings.WidgetTodos.RemoveAll(t => t.Done); _settings.Save(); TickWidgets(); }, null, double.NaN, 30);
            clear.Padding = new Thickness(14, 0, 14, 0); clear.HorizontalAlignment = HorizontalAlignment.Left; clear.VerticalAlignment = VerticalAlignment.Bottom;
            body.Children.Add(clear);
        }
        string last = "-";
        return () =>
        {
            var items = Todos;
            var key = string.Join("|", items.Select(t => t.Text + t.Done));
            if (key == last) return;
            last = key;
            left.Text = items.Count(t => !t.Done).ToString();
            list.Children.Clear();
            if (items.Count == 0) list.Children.Add(W(compact ? L.T("Нажмите, чтобы добавить дела") : L.T("Пока пусто"), 13, null, Dim));
            foreach (var t in (compact ? items.OrderBy(t => t.Done).Take(bigList ? 9 : 4) : items.Take(9)).ToList())
            {
                var item = t;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, compact ? 4 : 8), Background = Brushes.Transparent };
                var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.6), BorderBrush = accent, Margin = new Thickness(0, 0, 10, 0),
                                       Background = item.Done ? accent : Brushes.Transparent, Cursor = Cursors.Hand };
                if (item.Done) { var tick = Glyph("\uE73E", 9, Brushes.White); tick.HorizontalAlignment = HorizontalAlignment.Center; dot.Child = tick; }
                var text = W(item.Text, compact ? 13 : 14);
                if (item.Done) { text.TextDecorations = TextDecorations.Strikethrough; text.Opacity = 0.5; }
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                if (!compact)
                {
                    var del = Glyph("\uE711", 10, Dim); del.Cursor = Cursors.Hand; del.Margin = new Thickness(8, 0, 2, 0); del.Opacity = 0;
                    del.MouseLeftButtonUp += (_, e) => { e.Handled = true; _settings.WidgetTodos.Remove(item); _settings.Save(); TickWidgets(); };
                    row.MouseEnter += (_, _) => del.Opacity = 1; row.MouseLeave += (_, _) => del.Opacity = 0;
                    DockPanel.SetDock(del, Dock.Right); row.Children.Add(del);
                }
                row.Children.Add(text);
                dot.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    if (_widgetsEditing) return;
                    item.Done = !item.Done; _settings.Save();
                    // a little pop of the circle, then the list re-sorts
                    var s = new ScaleTransform(1, 1, 9, 9); dot.RenderTransform = s;
                    var a = new DoubleAnimation(1.35, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
                    s.BeginAnimation(ScaleTransform.ScaleXProperty, a); s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                    dot.Background = item.Done ? accent : Brushes.Transparent;
                    text.TextDecorations = item.Done ? TextDecorations.Strikethrough : null; text.Opacity = item.Done ? 0.5 : 1;
                    Dispatcher.BeginInvoke(TickWidgets, DispatcherPriority.Background);
                };
                list.Children.Add(row);
            }
        };
    }

    // ------------------------------------------------------------------ countdown

    private (string Title, DateTime Date) CountdownTarget
    {
        get
        {
            if (_settings.CountdownDate is { } d && d.Date >= DateTime.Today && _settings.CountdownTitle.Length > 0) return (_settings.CountdownTitle, d.Date);
            return (L.T("Новый год"), new DateTime(DateTime.Today.Year + 1, 1, 1));
        }
    }

    private Action CountdownWidget(Grid body, Border wash)
    {
        wash.Background = new LinearGradientBrush(Color.FromArgb(0x70, 0xBF, 0x5A, 0xF2), Color.FromArgb(0x40, 0xFF, 0x37, 0x5F), 45);
        var title = W("", 13, FontWeights.SemiBold, Solid(0xE0, 0xA8, 0xFF));
        var days = W("", 48, FontWeights.Light, null, big: true); days.Margin = new Thickness(-2, -2, 0, 0);
        var unit = W(L.T("дн."), 15, FontWeights.SemiBold, Dim); unit.Margin = new Thickness(6, 0, 0, 10); unit.VerticalAlignment = VerticalAlignment.Bottom;
        var date = W("", 11.5, FontWeights.SemiBold, Dim); date.VerticalAlignment = VerticalAlignment.Bottom;
        var s = new StackPanel(); s.Children.Add(title); s.Children.Add(Row(days, unit));
        body.Children.Add(s); body.Children.Add(date);
        return () =>
        {
            var (t, d) = CountdownTarget;
            title.Text = t;
            int n = (int)Math.Ceiling((d - DateTime.Now).TotalDays);
            days.Text = Math.Max(0, n).ToString();
            date.Text = n <= 0 ? L.T("Сегодня!") : d.ToString("d MMMM yyyy", Loc.Culture);
        };
    }

    private Action CountdownEditor(Grid body, Border wash)
    {
        var show = new Grid { Height = 120, VerticalAlignment = VerticalAlignment.Top };
        var update = CountdownWidget(show, wash);
        var (tbox, title) = Field(L.T("Название события"));
        var (dbox, date) = Field(L.T("Дата, например 31.12.2026"));
        var (t0, d0) = CountdownTarget;
        title.Text = _settings.CountdownTitle; date.Text = _settings.CountdownDate?.ToString("dd.MM.yyyy") ?? "";
        void Save()
        {
            if (DateTime.TryParseExact(date.Text.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || DateTime.TryParse(date.Text, Loc.Culture, DateTimeStyles.None, out d))
            {
                _settings.CountdownDate = d.Date;
                _settings.CountdownTitle = title.Text.Trim().Length > 0 ? title.Text.Trim() : L.T("Событие");
                _settings.Save();
            }
            update();
        }
        title.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Save(); };
        date.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Save(); };
        title.LostFocus += (_, _) => Save(); date.LostFocus += (_, _) => Save();
        var quick = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (label, when) in new[] { (L.T("Новый год"), new DateTime(DateTime.Today.Year + 1, 1, 1)), (L.T("Через неделю"), DateTime.Today.AddDays(7)), (L.T("Через месяц"), DateTime.Today.AddMonths(1)) })
        {
            var p = Pill(label, () => { title.Text = label; date.Text = when.ToString("dd.MM.yyyy"); Save(); }, null, double.NaN, 28);
            p.Padding = new Thickness(12, 0, 12, 0); p.Margin = new Thickness(0, 0, 6, 6);
            quick.Children.Add(p);
        }
        var s = new StackPanel { Margin = new Thickness(0, 126, 0, 0) };
        tbox.Margin = new Thickness(0, 0, 0, 8);
        s.Children.Add(tbox); s.Children.Add(dbox); s.Children.Add(quick);
        body.Children.Add(show); body.Children.Add(s);
        return update;
    }

    // ------------------------------------------------------------------ volume

    private Action VolumeWidget(Grid body, Border wash)
    {
        var fill = new Border { VerticalAlignment = VerticalAlignment.Bottom, Height = 0, Background = new SolidColorBrush(Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF)) };
        wash.Child = fill;
        var pct = W("", 30, FontWeights.Light, null, big: true);
        var icon = Glyph("\uE767", 20); icon.VerticalAlignment = VerticalAlignment.Bottom; icon.HorizontalAlignment = HorizontalAlignment.Left;
        var name = W(L.T("Громкость"), 12.5, FontWeights.SemiBold, Dim);
        var top = new StackPanel(); top.Children.Add(name); top.Children.Add(pct);
        body.Children.Add(top); body.Children.Add(icon);
        body.Background = Brushes.Transparent;
        float demo = 0.6f;
        float Get() => Settings.ReadOnly ? demo : _audio.Master;
        void Set(float v) { if (Settings.ReadOnly) demo = v; else { _audio.Master = v; if (_audio.MasterMuted && v > 0) _audio.MasterMuted = false; } }
        double FullHeight() => wash.ActualHeight > 0 ? wash.ActualHeight : SmallSide;
        void Show(bool animate)
        {
            float v = Get(); bool muted = !Settings.ReadOnly && _audio.MasterMuted;
            if (v < 0) { pct.Text = "—"; return; }
            pct.Text = muted ? L.T("Без звука") : $"{v * 100:0}%";
            pct.FontSize = muted ? 18 : 30;
            icon.Text = muted || v == 0 ? "\uE74F" : v < 0.34 ? "\uE993" : v < 0.67 ? "\uE994" : "\uE995";
            double h = FullHeight() * (muted ? 0 : v);
            if (animate) fill.BeginAnimation(HeightProperty, new DoubleAnimation(h, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            else { fill.BeginAnimation(HeightProperty, null); fill.Height = h; }
            // where the white fill covers the text, the text turns dark (like Control Center)
            bool covered = v > 0.62 && !muted;
            var dark = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E));
            if (covered) { pct.Foreground = dark; name.Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0x1C, 0x1C, 0x1E)); } else { pct.SetResourceReference(TextBlock.ForegroundProperty, "WFg"); name.SetResourceReference(TextBlock.ForegroundProperty, "WDim"); }
            if (v > 0.12 && !muted) icon.Foreground = dark; else icon.SetResourceReference(TextBlock.ForegroundProperty, "WFg");
        }
        bool dragging = false; Point downAt = default;
        body.MouseLeftButtonDown += (_, e) => { if (_widgetsEditing) return; e.Handled = true; dragging = true; downAt = e.GetPosition(body); body.CaptureMouse(); };
        body.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var p = e.GetPosition(wash);
            Set((float)Math.Clamp(1 - p.Y / FullHeight(), 0, 1));
            Show(false);
        };
        body.MouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            e.Handled = true; dragging = false; body.ReleaseMouseCapture();
            if ((e.GetPosition(body) - downAt).Length < 3)
            {
                // a tap on the speaker mutes; a tap elsewhere jumps there
                var p = e.GetPosition(icon);
                if (p.X >= -6 && p.Y >= -6 && p.X <= icon.ActualWidth + 6 && p.Y <= icon.ActualHeight + 6) { if (!Settings.ReadOnly) _audio.MasterMuted = !_audio.MasterMuted; }
                else Set((float)Math.Clamp(1 - e.GetPosition(wash).Y / FullHeight(), 0, 1));
                Show(true);
            }
        };
        body.LostMouseCapture += (_, _) => dragging = false;
        body.MouseWheel += (_, e) => { if (_widgetsEditing) return; e.Handled = true; Set(Math.Clamp(Get() + (e.Delta > 0 ? 0.04f : -0.04f), 0, 1)); Show(true); };
        return () => { if (!dragging) Show(true); };
    }

    // ------------------------------------------------------------------ apps

    private static readonly string[] DemoApps =
    [
        @"C:\Windows\explorer.exe", @"C:\Windows\System32\notepad.exe", @"C:\Windows\System32\cmd.exe", @"C:\Windows\System32\Taskmgr.exe",
        @"C:\Windows\System32\control.exe", @"C:\Windows\System32\charmap.exe", @"C:\Windows\regedit.exe", @"C:\Windows\System32\mstsc.exe",
    ];

    private Action LauncherWidget(Grid body, int max)
    {
        bool big = max > 8;
        var wrap = new WrapPanel();
        var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var hint = W(L.T("Добавьте приложения в док острова — они появятся здесь"), 13, null, Dim); hint.TextWrapping = TextWrapping.Wrap;
        var open = Pill(L.T("Открыть док"), () => OpenTab(TabHome), null, double.NaN, 28); open.Padding = new Thickness(12, 0, 12, 0); open.HorizontalAlignment = HorizontalAlignment.Left; open.Margin = new Thickness(0, 10, 0, 0);
        empty.Children.Add(hint); empty.Children.Add(open);
        if (big) { var s = new StackPanel(); s.Children.Add(Caption("\uE71D", L.T("Приложения"), Solid(0x64, 0xD2, 0xFF))); wrap.Margin = new Thickness(0, 12, 0, 0); s.Children.Add(wrap); body.Children.Add(s); }
        else body.Children.Add(wrap);
        body.Children.Add(empty);
        string last = "-";
        return () =>
        {
            var apps = (Settings.ReadOnly ? DemoApps.Where(File.Exists) : _settings.DockApps.AsEnumerable()).Take(max).ToList();
            var key = string.Join("|", apps);
            if (key == last) return;
            last = key;
            wrap.Children.Clear();
            empty.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var path in apps)
            {
                var p = path;
                var img = new Image { Width = big ? 40 : 36, Height = big ? 40 : 36, Source = ShellIcons.Get(p, 64) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                var tile = new StackPanel { Width = big ? 70 : 76.5, Margin = new Thickness(0, 0, 0, big ? 10 : 6) };
                var plate = new Border { Width = big ? 58 : 56, Height = big ? 58 : 56, CornerRadius = new CornerRadius(15), Child = img, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center };
                plate.SetResourceReference(Border.BackgroundProperty, "WFill");
                tile.Children.Add(plate);
                string name = AppIndex.IsStoreApp(p) ? p : System.IO.Path.GetFileNameWithoutExtension(p);
                if (big) { var n = W(name, 11, null, Dim); n.HorizontalAlignment = HorizontalAlignment.Center; n.Margin = new Thickness(0, 4, 0, 0); n.MaxWidth = 66; tile.Children.Add(n); }
                tile.ToolTip = name;
                var press = WKey(tile, Brushes.Transparent, null, () => AppIndex.Launch(p), tile.Width, big ? 80 : 60, 15);
                press.Margin = new Thickness(0);
                wrap.Children.Add(press);
            }
        };
    }

    // ------------------------------------------------------------------ moon

    private static double MoonPhase(DateTime utc) =>
        ((utc - new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc)).TotalDays % 29.530588853 + 29.530588853) % 29.530588853 / 29.530588853;

    private static Geometry MoonLit(double p, double r)
    {
        double cx = r, cy = r, rx = r * Math.Abs(Math.Cos(2 * Math.PI * p));
        bool waxing = p < 0.5, gibbous = p > 0.25 && p < 0.75;
        var fig = new PathFigure { StartPoint = new Point(cx, cy - r), IsClosed = true, IsFilled = true };
        // the lit half: the right side while it waxes, the left while it wanes
        fig.Segments.Add(new ArcSegment(new Point(cx, cy + r), new Size(r, r), 0, false, waxing ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
        // back along the terminator: bulging into the lit half (crescent) or out of it (gibbous)
        bool bulgeRight = waxing ? !gibbous : gibbous;
        fig.Segments.Add(new ArcSegment(new Point(cx, cy - r), new Size(Math.Max(0.01, rx), r), 0, false, bulgeRight ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true));
        return new PathGeometry([fig]);
    }

    private Action MoonWidget(Grid body)
    {
        const double R = 34;
        var disc = new Grid { Width = R * 2, Height = R * 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        disc.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)) });
        var lit = new System.Windows.Shapes.Path { Fill = new RadialGradientBrush(Color.FromRgb(0xFA, 0xF7, 0xEE), Color.FromRgb(0xD8, 0xD2, 0xC2)) { GradientOrigin = new Point(0.4, 0.35) },
                                                   Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xF8, 0xE0), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.6 } };
        disc.Children.Add(lit);
        var name = W("", 13.5, FontWeights.SemiBold); name.TextWrapping = TextWrapping.Wrap;
        var light = W("", 11.5, null, Dim); var full = W("", 11.5, null, Dim);
        var cap = Caption("\uE708", L.T("Луна"), Solid(0xE5, 0xE0, 0xD0));
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom }; info.Children.Add(name); info.Children.Add(light); info.Children.Add(full);
        body.Children.Add(cap); body.Children.Add(disc); body.Children.Add(info);
        disc.Margin = new Thickness(0, 4, 0, 0);
        return () =>
        {
            double p = MoonPhase(DateTime.UtcNow);
            lit.Data = MoonLit(p, R);
            name.Text = p < 0.03 || p > 0.97 ? L.T("Новолуние") : p < 0.22 ? L.T("Растущий серп") : p < 0.28 ? L.T("Первая четверть") : p < 0.47 ? L.T("Растущая луна")
                      : p < 0.53 ? L.T("Полнолуние") : p < 0.72 ? L.T("Убывающая луна") : p < 0.78 ? L.T("Последняя четверть") : L.T("Убывающий серп");
            light.Text = L.F("освещена на {0:0}%", (1 - Math.Cos(2 * Math.PI * p)) / 2 * 100);
            double toFull = ((0.5 - p + 1) % 1) * 29.53;
            full.Text = toFull < 0.6 ? L.T("сегодня полнолуние") : L.F("до полнолуния {0:0} дн.", toFull);
        };
    }

    // ------------------------------------------------------------------ month

    private Action MonthWidget(Grid body, bool big = false)
    {
        var red = Solid(0xFF, 0x45, 0x3A);
        var grid = new Grid();
        for (int i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        var events = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var title = W("", big ? 20 : 13, FontWeights.SemiBold, big ? null : red);
        DateTime selected = DateTime.Today, builtFor = DateTime.Today;
        Action dayTexts = () => { };
        if (big)
        {
            var s = new StackPanel(); s.Children.Add(title); grid.Margin = new Thickness(0, 10, 0, 0); s.Children.Add(grid); s.Children.Add(events);
            body.Children.Add(s);
        }
        else
        {
            var weekday = W("", 12.5, FontWeights.SemiBold, red); var day = W("", 40, FontWeights.Light, null, big: true); day.Margin = new Thickness(-1, -6, 0, 0);
            var left = new StackPanel { Width = 92 }; left.Children.Add(weekday); left.Children.Add(day); left.Children.Add(title);
            title.Foreground = null; title.SetResourceReference(TextBlock.ForegroundProperty, "WDim"); title.FontSize = 12;
            var dock = new DockPanel(); DockPanel.SetDock(left, Dock.Left); dock.Children.Add(left); dock.Children.Add(grid);
            body.Children.Add(dock);
            dayTexts = () => { weekday.Text = DateTime.Now.ToString("dddd", Loc.Culture).ToUpper(Loc.Culture); day.Text = DateTime.Now.Day.ToString(); };
        }
        string last = "";
        void Build()
        {
            var today = DateTime.Today;
            if (builtFor != today) { builtFor = today; selected = today; }
            dayTexts();
            var key = today.ToString("d") + selected.ToString("d") + _calendar.Upcoming(31).Count();
            if (key == last) return;
            last = key;
            title.Text = big ? today.ToString("MMMM yyyy", Loc.Culture) : today.ToString("MMMM", Loc.Culture);
            if (big && title.Text.Length > 0) title.Text = char.ToUpper(title.Text[0], Loc.Culture) + title.Text[1..];
            grid.Children.Clear(); grid.RowDefinitions.Clear();
            var first = new DateTime(today.Year, today.Month, 1);
            int offset = ((int)first.DayOfWeek + 6) % 7;
            int days = DateTime.DaysInMonth(today.Year, today.Month), rows = (offset + days + 6) / 7 + 1;
            double rh = big ? 34 : (SmallSide - 28) / 7.0;
            for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(rh) });
            var busy = _calendar.Upcoming(31).Select(e => e.Start.Date).ToHashSet();
            var names = Loc.Culture.DateTimeFormat.AbbreviatedDayNames;
            for (int i = 0; i < 7; i++)
            {
                var h = W(names[(i + 1) % 7][..1].ToUpper(Loc.Culture), big ? 11.5 : 9.5, FontWeights.SemiBold, Dim);
                h.HorizontalAlignment = HorizontalAlignment.Center; h.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(h, i); grid.Children.Add(h);
            }
            for (int d = 1; d <= days; d++)
            {
                var date = new DateTime(today.Year, today.Month, d);
                int idx = offset + d - 1;
                bool isToday = date == today, isSel = big && date == selected && !isToday;
                double size = big ? 30 : 17;
                var t = W(d.ToString(), big ? 14 : 10, isToday ? FontWeights.Bold : FontWeights.Normal, isToday ? Brushes.White : null);
                t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
                if (date < today && !isToday) t.Opacity = 0.45;
                var cell = new Grid { Width = size, Height = size, Background = Brushes.Transparent };
                if (isToday) cell.Children.Add(new Ellipse { Fill = red });
                if (isSel) { var ring = new Ellipse { StrokeThickness = 1.5 }; ring.SetResourceReference(Shape.StrokeProperty, "WFg"); cell.Children.Add(ring); }
                cell.Children.Add(t);
                if (busy.Contains(date) && !isToday)
                    cell.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = red, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, big ? -2 : -3) });
                if (big)
                {
                    cell.Cursor = Cursors.Hand;
                    cell.MouseLeftButtonUp += (_, e) => { e.Handled = true; selected = date; last = ""; Build(); };
                }
                Grid.SetRow(cell, idx / 7 + 1); Grid.SetColumn(cell, idx % 7);
                grid.Children.Add(cell);
            }
            if (big)
            {
                events.Children.Clear();
                var day = _calendar.Upcoming(31).Where(e => e.Start.Date == selected).Take(3).ToList();
                events.Children.Add(W(selected == today ? L.T("Сегодня") : selected.ToString("d MMMM, dddd", Loc.Culture), 12, FontWeights.SemiBold, Dim));
                if (day.Count == 0) events.Children.Add(W(L.T("Нет событий"), 13.5));
                foreach (var e in day)
                {
                    var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = red, Margin = new Thickness(0, 2, 8, 2) };
                    var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                    var when = W(e.AllDay ? L.T("весь день") : e.Start.ToString("HH:mm"), 12, null, Dim); DockPanel.SetDock(when, Dock.Right);
                    DockPanel.SetDock(bar, Dock.Left);
                    row.Children.Add(bar); row.Children.Add(when); row.Children.Add(W(e.Title, 13.5, FontWeights.SemiBold));
                    events.Children.Add(row);
                }
            }
        }
        return Build;
    }

    // ------------------------------------------------------------------ processes

    private static List<(string Name, long Bytes)> _topProcs = [];
    private static DateTime _topAt;
    private static bool _topBusy;

    private void SampleTop()
    {
        if (_topBusy || (DateTime.Now - _topAt).TotalSeconds < 5) return;
        _topBusy = true; _topAt = DateTime.Now;
        if (Settings.ReadOnly)
        {
            _topProcs = [("chrome", 1_850_000_000), ("Code", 920_000_000), ("Discord", 610_000_000), ("Spotify", 380_000_000), ("explorer", 210_000_000),
                         ("DynamicIsland", 140_000_000), ("Telegram", 120_000_000), ("steam", 95_000_000), ("OneDrive", 60_000_000), ("Widgets", 40_000_000)];
            _topBusy = false;
            return;
        }
        _ = Task.Run(() =>
        {
            try
            {
            var list = new Dictionary<string, long>();
            foreach (var p in Process.GetProcesses())
            {
                try { list[p.ProcessName] = list.GetValueOrDefault(p.ProcessName) + p.WorkingSet64; } catch { }
                finally { p.Dispose(); }
            }
            _topProcs = list.Where(kv => kv.Key is not "Idle" and not "System" and not "Memory Compression" and not "Registry")
                            .OrderByDescending(kv => kv.Value).Take(12).Select(kv => (kv.Key, kv.Value)).ToList();
            }
            catch (Exception e) { Log.Write("processes widget: " + e.Message); }
            finally { _topBusy = false; }
        });
    }

    private Action TopWidget(Grid body, int n)
    {
        var accent = Solid(0xFF, 0x6B, 0x9D);
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var s = new StackPanel(); s.Children.Add(Caption("\uE9D9", L.T("Процессы"), accent)); s.Children.Add(list);
        body.Children.Add(s);
        var rows = new List<(TextBlock Name, TextBlock Size, Border Fill, Grid Track)>();
        for (int i = 0; i < n; i++)
        {
            var name = W("", 12.5, FontWeights.SemiBold); var size = W("", 11.5, null, Dim);
            var (track, fill) = Bar(4, new LinearGradientBrush(Color.FromRgb(0xFF, 0x6B, 0x9D), Color.FromRgb(0xBF, 0x5A, 0xF2), 0));
            var head = new DockPanel(); DockPanel.SetDock(size, Dock.Right); head.Children.Add(size); head.Children.Add(name);
            var r = new StackPanel { Margin = new Thickness(0, 0, 0, n > 4 ? 9 : 5) }; r.Children.Add(head); r.Children.Add(track); track.Margin = new Thickness(0, 3, 0, 0);
            list.Children.Add(r);
            rows.Add((name, size, fill, track));
        }
        return () =>
        {
            SampleTop();
            var top = _topProcs;
            long max = top.Count > 0 ? top[0].Bytes : 1;
            for (int i = 0; i < rows.Count; i++)
            {
                var (name, size, fill, track) = rows[i];
                bool has = i < top.Count;
                name.Text = has ? top[i].Name : ""; size.Text = has ? Bytes(top[i].Bytes) : "";
                double target = has && track.ActualWidth > 0 ? track.ActualWidth * top[i].Bytes / max : 0;
                fill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        };
    }

    // ------------------------------------------------------------------ breathing

    private Action BreatheWidget(Grid body, Border wash)
    {
        wash.Background = new RadialGradientBrush(Color.FromArgb(0x50, 0x30, 0xD1, 0xC8), Color.FromArgb(0x10, 0x30, 0x8A, 0xD1));
        var flower = new Canvas { Width = 100, Height = 100, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -14, 0, 0) };
        var petals = new List<Ellipse>();
        for (int i = 0; i < 6; i++)
        {
            var e = new Ellipse { Width = 46, Height = 46, Fill = new SolidColorBrush(Color.FromArgb(0x70, (byte)(0x30 + i * 6), 0xC8, (byte)(0xD8 - i * 10))) };
            petals.Add(e); flower.Children.Add(e);
        }
        var rotate = new RotateTransform(0, 50, 50); flower.RenderTransform = rotate;
        var text = W(L.T("Нажмите, чтобы начать"), 11.5, FontWeights.SemiBold, Dim); text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Bottom;
        text.TextTrimming = TextTrimming.None; text.TextWrapping = TextWrapping.Wrap; text.TextAlignment = TextAlignment.Center;
        body.Children.Add(flower); body.Children.Add(text);
        body.Background = Brushes.Transparent;
        DateTime? started = null;
        bool ticking = false;
        void Pose(double s)
        {
            double r = 4 + 20 * s, scale = 0.55 + 0.45 * s;
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3;
                var e = petals[i];
                e.Width = e.Height = 46 * scale;
                Canvas.SetLeft(e, 50 + Math.Cos(a) * r - e.Width / 2);
                Canvas.SetTop(e, 50 + Math.Sin(a) * r - e.Height / 2);
            }
            rotate.Angle = 60 * s;
        }
        void Frame(object? sender, EventArgs e)
        {
            // stopped, closed or hidden under windows: no more frames
            if (started is not { } t0 || PresentationSource.FromVisual(body) == null || !body.IsVisible)
            {
                CompositionTarget.Rendering -= Frame; ticking = false; started = null; Pose(0.55); text.Text = L.T("Нажмите, чтобы начать"); return;
            }
            double t = (DateTime.Now - t0).TotalSeconds;
            if (t > 64) { started = null; return; }
            double phase = t % 8;   // 4 s in, 4 s out
            Pose((1 - Math.Cos(Math.PI * phase / 4)) / 2);
            text.Text = phase < 4 ? L.T("Вдох") : L.T("Выдох");
        }
        body.MouseLeftButtonUp += (_, e) =>
        {
            if (_widgetsEditing) return;
            e.Handled = true;
            if (started != null) { started = null; return; }
            started = DateTime.Now;
            if (!ticking) { ticking = true; CompositionTarget.Rendering += Frame; }   // a quick second tap must not add a second handler
        };
        Pose(0.55);
        return () => { };
    }
}

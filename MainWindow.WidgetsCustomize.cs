using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DynamicIsland.Services;
using DynamicIsland.Views;

namespace DynamicIsland;

/// <summary>
/// Making the widgets one's own: the grid they sit on (visible while editing, with the landing place lit up while
/// you drag), per-widget size, colour, accent and glass, widget options, ready-made sets, a first-time hint.
/// </summary>
public partial class MainWindow
{
    /// <summary>The grid's measures (setting "WidgetGrid").</summary>
    private static GridSpec Spec = GridLayout.A;
    private static double Step => Spec.Step;

    // ------------------------------------------------------------------ sizes

    /// <summary>The sizes a widget looks good in (S, M, L).</summary>
    private static string SizesOf(WidgetKind k) => k.Id switch
    {
        "music" => "SML", "weather" => "SML", "clock" => "SL", "calendar" => "SM", "month" => "ML", "worldclock" => "ML",
        "sun" => "SM", "photos" => "SML", "notes" => "SML", "todo" or "reminders" or "clipboard" or "launcher" or "top" => "ML",
        "calc" => "L", "system" => "SML", "breathe" => "SML",
        _ => "SM",
    };

    private static char Letter(WidgetSize s) => s switch { WidgetSize.Small => 'S', WidgetSize.Medium => 'M', _ => 'L' };
    private static WidgetSize FromLetter(char c) => c switch { 'S' => WidgetSize.Small, 'L' => WidgetSize.Large, _ => WidgetSize.Medium };

    /// <summary>The size the user chose for this widget (if it allows it), else its own.</summary>
    private WidgetSize SizeFor(WidgetKind k) =>
        _settings.WidgetSizeOf.TryGetValue(k.Id, out var s) && s.Length == 1 && SizesOf(k).Contains(s[0]) ? FromLetter(s[0]) : k.Size;

    /// <summary>What a widget is being built as right now (its size, its accent): the builders read these.</summary>
    private WidgetSize _buildSize = WidgetSize.Medium;
    private Color? _buildAccent;

    private Color? AccentOf(string id) =>
        _settings.WidgetAccentOf.TryGetValue(id, out var hex) && TryColor(hex, out var c) ? c : null;

    private double GlassOf(string id) => _settings.WidgetGlassOf.TryGetValue(id, out var v) ? Math.Clamp(v, 0, 1) : 0.5;

    private static bool TryColor(string hex, out Color c)
    {
        c = default;
        try { c = (Color)ColorConverter.ConvertFromString(hex.StartsWith('#') ? hex : "#" + hex); return true; } catch { return false; }
    }

    /// <summary>A widget's accent: the user's colour if they picked one, else the widget's own.</summary>
    private Brush Acc(byte r, byte g, byte b) => _buildAccent is { } c ? new SolidColorBrush(c) : Solid(r, g, b);
    private Color AccC(Color own) => _buildAccent ?? own;

    // ------------------------------------------------------------------ the grid

    /// <summary>
    /// The grid on each monitor (work area in DIPs and its grid). The grid's measures are a setting: two variants on
    /// an 8 px module (see <see cref="GridLayout"/>).
    /// </summary>
    private IReadOnlyList<(Rect Work, WidgetGridArea Grid)> Grids()
    {
        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return System.Windows.Forms.Screen.AllScreens.OrderByDescending(s => s.Primary).Select(s =>
        {
            var wa = s.WorkingArea;
            var r = new Rect(m.Transform(new Point(wa.Left, wa.Top)), m.Transform(new Point(wa.Right, wa.Bottom)));
            return (r, GridLayout.For(r, Spec));
        }).ToList();
    }

    /// <summary>The grid of the monitor the rectangle is (mostly) on.</summary>
    private WidgetGridArea GridAt(Rect r)
    {
        var grids = Grids();
        return grids[WallpaperLayout.MonitorAt(grids.Select(g => g.Work).ToList(), r)].Grid;
    }

    /// <summary>The main monitor's grid (first cell, size) — for code that places on the main screen.</summary>
    private (double X0, double Y0, int Cols, int Rows) WidgetGrid()
    {
        var g = Grids()[0].Grid;
        return (g.X0, g.Y0, g.Cols, g.Rows);
    }

    private Rect HomeRect(string id, DesktopWidgetWindow w) => _openWidgets.TryGetValue(id, out var o) ? o.Home : w.CardRect;

    private List<Rect> OthersThan(string? id) => _widgets.Where(p => p.Key != id).Select(p => HomeRect(p.Key, p.Value)).ToList();

    private Rect? GalleryBlock => _widgetBar != null ? _widgetBarRect : null;

    private Rect SnapRect(Rect r) => GridLayout.Snap(r, GridAt(r));

    private bool PlaceFree(Rect r, string? except) => GridLayout.Free(r, OthersThan(except), GalleryBlock);

    /// <summary>The free grid place nearest to where it was dropped (it never lands on top of another widget).</summary>
    private Rect FreeNear(Rect want, string? except) => GridLayout.Nearest(want, GridAt(want), OthersThan(except), GalleryBlock) ?? SnapRect(want);

    /// <summary>
    /// Puts a widget on the grid at (or near) the place it was dropped on: widgets it would cover glide aside to
    /// the free places nearest to them. If they can't all move, it takes the nearest free place itself.
    /// </summary>
    private void Land(DesktopWidgetWindow w, Rect want, bool glide = true)
    {
        var grid = GridAt(want);
        var target = GridLayout.Snap(want, grid);
        var others = _widgets.Where(p => p.Key != w.Id).ToDictionary(p => p.Key, p => HomeRect(p.Key, p.Value));
        var moves = GalleryBlock is { } gb && target.IntersectsWith(gb) ? null : GridLayout.MakeRoom(target, others, grid, GalleryBlock);
        if (moves == null) { target = FreeNear(want, w.Id); moves = []; }
        Log.Write($"widgets: {w.Id} lands at {target.X:0},{target.Y:0} {target.Width:0}x{target.Height:0}; moved aside: {string.Join(", ", moves.Select(m => $"{m.Key}→{m.Value.X:0},{m.Value.Y:0}"))}");
        foreach (var (id, r) in moves)
        {
            if (!_widgets.TryGetValue(id, out var o)) continue;
            o.Spring.To(r);
            _settings.WidgetPlaces[id] = [r.X, r.Y];
        }
        if (glide) w.Spring.To(target); else { w.CardX = target.X; w.CardY = target.Y; }
        _settings.WidgetPlaces[w.Id] = [target.X, target.Y];
        RememberLayout();
        _settings.Save();
    }

    // ------------------------------------------------------------------ a layout per set of monitors

    private string _layoutKey = "";

    private string CurrentLayoutKey() =>
        GridLayout.LayoutKey(System.Windows.Forms.Screen.AllScreens.Select(s => new Rect(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)));

    /// <summary>The places now are the layout of this set of monitors.</summary>
    private void RememberLayout()
    {
        if (_layoutKey.Length == 0) _layoutKey = CurrentLayoutKey();
        _settings.WidgetLayouts[_layoutKey] = new Dictionary<string, double[]>(_settings.WidgetPlaces);
    }

    /// <summary>
    /// Monitors changed: the layout that belongs to the new set comes back (if there is one); widgets that would
    /// be off every screen land on the grid of the main one — none is ever lost.
    /// </summary>
    private void OnMonitorsChanged()
    {
        var key = CurrentLayoutKey();
        if (key == _layoutKey) { KeepWidgetsOnScreen(); return; }
        RememberLayout();
        _layoutKey = key;
        if (_settings.WidgetLayouts.TryGetValue(key, out var saved))
        {
            foreach (var (id, at) in saved)
                if (at.Length == 2) _settings.WidgetPlaces[id] = at;
            foreach (var (id, w) in _widgets)
                if (_settings.WidgetPlaces.TryGetValue(id, out var p) && OnSomeScreen(p[0], p[1], w.CardW, w.CardH)) { w.CardX = p[0]; w.CardY = p[1]; }
        }
        KeepWidgetsOnScreen();
        Log.Write($"widgets: monitors changed, layout {(saved != null ? "restored" : "kept")}");
    }

    private void KeepWidgetsOnScreen()
    {
        foreach (var (id, w) in _widgets.ToList())
            if (!OnSomeScreen(w.CardX, w.CardY, w.CardW, w.CardH))
            {
                var (x, y) = FreeSpot(w.CardW, w.CardH);
                w.CardX = x; w.CardY = y; _settings.WidgetPlaces[id] = [x, y];
            }
        RememberLayout();
        _settings.Save();
    }

    /// <summary>
    /// Once, after the grid changed (finer module, sizes in cells): every widget onto the new grid, in reading
    /// order, none on top of another.
    /// </summary>
    private void ReflowAll()
    {
        var placed = new List<Rect>();
        foreach (var w in _widgets.Values.OrderBy(w => w.CardY).ThenBy(w => w.CardX).ToList())
        {
            var grid = GridAt(w.CardRect);
            var spot = GridLayout.Nearest(w.CardRect, grid, placed) ?? GridLayout.First(w.CardW, w.CardH, grid, placed) ?? w.CardRect;
            w.CardX = spot.X; w.CardY = spot.Y;
            _settings.WidgetPlaces[w.Id] = [spot.X, spot.Y];
            placed.Add(spot);
        }
        RememberLayout();
        _settings.Save();
    }

    // ------------------------------------------------------------------ edit mode: the grid hint, add, done

    private readonly List<(Window Win, Border Target, Rect Work)> _gridOverlays = [];

    /// <summary>
    /// Edit mode: on every monitor a barely-there grid (one faint rounded square per cell) over a slightly dimmed
    /// desktop; the place a dragged widget will land lights up. "+" opens the gallery, "Done" (or a click on an
    /// empty spot, or Esc in the gallery) ends editing.
    /// </summary>
    private void ShowGridOverlay(bool on)
    {
        if (!on)
        {
            foreach (var o in _gridOverlays.ToList()) o.Win.Close();
            _gridOverlays.Clear();
            return;
        }
        if (_gridOverlays.Count > 0) return;
        bool first = true;
        foreach (var (a, g) in Grids())
        {
            var canvas = new Canvas { Width = a.Width, Height = a.Height, Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)) };
            double cr = Math.Round(WidgetCard.Radius * g.Spec.Cell / g.Spec.Span(2));
            for (int c = 0; c < g.Cols; c++)
                for (int r = 0; r < g.Rows; r++)
                {
                    var cell = new Rectangle
                    {
                        Width = g.Spec.Cell, Height = g.Spec.Cell, RadiusX = cr, RadiusY = cr, IsHitTestVisible = false,
                        Stroke = new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1,
                        Fill = new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)),
                    };
                    Canvas.SetLeft(cell, g.X0 - a.Left + c * g.Spec.Step); Canvas.SetTop(cell, g.Y0 - a.Top + r * g.Spec.Step);
                    canvas.Children.Add(cell);
                }
            var target = new Border
            {
                CornerRadius = new CornerRadius(WidgetCard.Radius), BorderThickness = new Thickness(2), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            };
            canvas.Children.Add(target);
            if (first)
            {
                // "+" at the top left, "Done" at the top right (the island is in the middle)
                var plus = WKey(Glyph("", 16, Brushes.White), new SolidColorBrush(Color.FromArgb(0x66, 0x30, 0x30, 0x36)), null, () => ShowWidgetGallery(true), 44, 44,
                                name: L.T("Добавить виджет"), evenWhenEditing: true);
                Canvas.SetLeft(plus, 24); Canvas.SetTop(plus, 16);
                var done = WKey(W(L.T("Готово"), 14, FontWeights.SemiBold, Brushes.White), Solid(0x0A, 0x84, 0xFF), null, () => EditWidgets(false), 96, 40, evenWhenEditing: true);
                Canvas.SetLeft(done, a.Width - 96 - 24); Canvas.SetTop(done, 18);
                var hint = new TextBlock
                {
                    Text = L.T("Перетащите виджет по сетке • потяните за угол, чтобы сменить размер • нажмите на виджет, чтобы настроить"),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), FontSize = 13, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                };
                hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(hint, (a.Width - hint.DesiredSize.Width) / 2); Canvas.SetTop(hint, a.Height - 30);
                canvas.Children.Add(plus); canvas.Children.Add(done); canvas.Children.Add(hint);
                first = false;
            }
            var win = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
                ResizeMode = ResizeMode.NoResize, Left = a.Left, Top = a.Top, Width = a.Width, Height = a.Height, Content = canvas, Title = "Atoll widget grid", Opacity = 0,
            };
            win.SourceInitialized += (_, _) =>
            {
                var h = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                Native.SetWindowLong(h, Native.GWL_EXSTYLE, Native.GetWindowLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | 0x08000000);
            };
            canvas.MouseLeftButtonUp += (_, e) => { if (ReferenceEquals(e.OriginalSource, canvas)) EditWidgets(false); };
            win.Show();
            win.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 220)));
            _gridOverlays.Add((win, target, a));
        }
        // the widgets stay above the grid
        foreach (var w in _widgets.Values) if (w.Front) { w.Front = false; w.Front = true; }
    }

    /// <summary>Lights up a place on the grid (null: nothing).</summary>
    private void ShowGridGhost(Rect? place)
    {
        foreach (var (_, target, a) in _gridOverlays)
        {
            if (place is not { } t || !a.Contains(new Point(t.X + t.Width / 2, t.Y + t.Height / 2))) { target.Visibility = Visibility.Collapsed; continue; }
            target.Width = t.Width; target.Height = t.Height;
            Canvas.SetLeft(target, t.X - a.Left); Canvas.SetTop(target, t.Y - a.Top);
            target.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Where a dragged widget would land.</summary>
    private void ShowGridTarget(DesktopWidgetWindow? w) => ShowGridGhost(w == null ? null : SnapRect(w.CardRect));

    // ------------------------------------------------------------------ resizing by the corner

    private char? _resizeTo;

    /// <summary>While the corner is dragged: the allowed size nearest to it lights up on the grid.</summary>
    private void ResizingWidget(DesktopWidgetWindow w, Point cursor)
    {
        var kind = WidgetKinds.First(k => k.Id == w.Id);
        var letters = SizesOf(kind);
        if (letters.Length < 2) return;
        var sizes = letters.Select(c => { var (sw, sh) = SizeOf(FromLetter(c)); return new Size(sw, sh); }).ToList();
        int i = GridLayout.NearestSize(new Size(Math.Max(1, cursor.X - w.CardX), Math.Max(1, cursor.Y - w.CardY)), sizes);
        _resizeTo = letters[i];
        ShowGridGhost(SnapRect(new Rect(w.CardX, w.CardY, sizes[i].Width, sizes[i].Height)));
    }

    private void ResizedWidget(DesktopWidgetWindow w)
    {
        ShowGridGhost(null);
        if (_resizeTo is not { } letter) return;
        _resizeTo = null;
        var kind = WidgetKinds.First(k => k.Id == w.Id);
        if (letter == Letter(SizeFor(kind))) return;
        _settings.WidgetSizeOf[w.Id] = letter.ToString();
        RebuildWidget(w.Id);
    }

    // ------------------------------------------------------------------ rebuilding one widget (new size, colours)

    private void RebuildWidget(string id)
    {
        if (!_widgets.TryGetValue(id, out var old)) return;
        _openWidgets.Remove(id);
        var at = new Point(old.CardX, old.CardY);
        _widgets.Remove(id); _widgetUpdaters.Remove(id);
        old.Close();
        ShowWidget(id);
        if (!_widgets.TryGetValue(id, out var w)) return;
        // stays where it was; neighbours it would now cover move aside
        Land(w, new Rect(at.X, at.Y, w.CardW, w.CardH), glide: false);
        Materialize(w);
    }

    // ------------------------------------------------------------------ the widget editor (a glass popover beside the widget)

    private Window? _widgetEditor;

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xFF, 0x45, 0x3A), Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xFF, 0xD6, 0x0A), Color.FromRgb(0x30, 0xD1, 0x58),
        Color.FromRgb(0x63, 0xE6, 0xE2), Color.FromRgb(0x40, 0xC8, 0xE0), Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x5E, 0x5C, 0xE6),
        Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0xFF, 0x37, 0x5F), Color.FromRgb(0xAC, 0x8E, 0x68), Color.FromRgb(0x8E, 0x8E, 0x93),
    ];

    /// <summary>
    /// "Edit widget": size, look (theme or any colour), accent, how much glass, and the widget's own options —
    /// every change shows on the widget at once. Opens next to the widget it edits.
    /// </summary>
    private void ShowWidgetEditor(string id)
    {
        // the old editor is let go before it closes: its "lost the focus" while closing must not close it again
        var oldEditor = _widgetEditor;
        _widgetEditor = null;
        oldEditor?.Close();
        if (!_widgets.TryGetValue(id, out var target)) return;
        var kind = WidgetKinds.First(k => k.Id == id);
        bool glass = WidgetGlass;
        var white = Brushes.White;
        var dim = new SolidColorBrush(Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF));
        var stack = new StackPanel();
        TextBlock Head(string t) => new() { Text = t, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = white, Margin = new Thickness(2, 16, 0, 8) };

        // title row: icon, name, Done
        var icon = new TextBlock { Text = kind.Icon, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 16, Foreground = white, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var title = new TextBlock { Text = kind.Name, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = white, VerticalAlignment = VerticalAlignment.Center, FontFamily = Display };
        Window? self = null;
        var done = WKey(W(L.T("Готово"), 13.5, FontWeights.SemiBold, white), Solid(0x0A, 0x84, 0xFF), null, () => self?.Close(), 86, 34, evenWhenEditing: true);
        var head = new DockPanel(); DockPanel.SetDock(done, Dock.Right);
        head.Children.Add(done); head.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, title } });
        stack.Children.Add(head);

        // size: a segmented control of the sizes this widget has
        var sizes = SizesOf(kind);
        if (sizes.Length > 1)
        {
            stack.Children.Add(Head(L.T("Размер")));
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            var current = SizeFor(kind);
            foreach (var ch in sizes)
            {
                var size = FromLetter(ch);
                var (sw, sh) = SizeOf(size);
                var shape = new Border { Width = sw / 12, Height = sh / 12, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1.4), BorderBrush = white, Margin = new Thickness(0, 0, 0, 5), HorizontalAlignment = HorizontalAlignment.Center };
                var label = new TextBlock { Text = size switch { WidgetSize.Small => L.T("Маленький"), WidgetSize.Medium => L.T("Средний"), _ => L.T("Большой") }, FontSize = 12, Foreground = white, HorizontalAlignment = HorizontalAlignment.Center };
                var cellContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { shape, label } };
                bool on = size == current;
                var cell = WKey(cellContent, on ? Solid(0xFF, 0xFF, 0xFF, 0x40) : Brushes.Transparent, null, () =>
                {
                    _settings.WidgetSizeOf[id] = ch.ToString();
                    _settings.Save();
                    RebuildWidget(id);
                    ShowWidgetEditor(id);
                }, 88, 64, 14, evenWhenEditing: true);
                cell.Margin = new Thickness(0, 0, 4, 0);
                seg.Children.Add(cell);
            }
            stack.Children.Add(GlassGroup(seg, 20));
        }

        // look: themes, then any colour
        stack.Children.Add(Head(L.T("Стиль")));
        stack.Children.Add(ThemePicker(id, 26, () => ShowWidgetEditor(id), withSame: true));
        var colors = new WrapPanel { Margin = new Thickness(2, 2, 0, 0) };
        string own = _settings.WidgetThemeOf.GetValueOrDefault(id, "");
        foreach (var c in Palette)
        {
            string themeId = "custom:" + c.ToString().Substring(3);
            bool on = own == themeId;
            var dot = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, Background = new SolidColorBrush(c),
                BorderThickness = new Thickness(on ? 2.5 : 1), BorderBrush = on ? white : new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), ToolTip = L.T("Свой цвет"),
            };
            dot.MouseLeftButtonUp += (_, e) => { e.Handled = true; SetWidgetTheme(id, themeId); ShowWidgetEditor(id); };
            colors.Children.Add(dot);
        }
        stack.Children.Add(colors);

        // how much glass: from barely tinted to deep colour
        var slider = new Views.Scrubber { Label = L.T("Насыщенность стекла"), Format = v => $"{v * 100:0}%", Minimum = 0, Maximum = 1, Step = .01, Value = GlassOf(id), Margin = new Thickness(0, 12, 0, 0) };
        slider.ValueChanged += (_, e) =>
        {
            _settings.WidgetGlassOf[id] = Math.Round(e.NewValue, 2);
            if (_widgets.TryGetValue(id, out var w)) w.ApplyTheme(WidgetThemeOf(id), GlassOf(id));
            _settings.SaveSoon();
        };
        stack.Children.Add(slider);

        // accent: the colour of its title, rings and levels
        stack.Children.Add(Head(L.T("Акцент")));
        var accents = new WrapPanel { Margin = new Thickness(2, 0, 0, 0) };
        string ownAccent = _settings.WidgetAccentOf.GetValueOrDefault(id, "");
        var auto = new Border
        {
            Height = 22, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
            Background = ownAccent == "" ? Solid(0xFF, 0xFF, 0xFF, 0x40) : Solid(0xFF, 0xFF, 0xFF, 0x18),
            Child = new TextBlock { Text = L.T("Свой"), FontSize = 11.5, Foreground = white, VerticalAlignment = VerticalAlignment.Center },
        };
        auto.MouseLeftButtonUp += (_, e) => { e.Handled = true; _settings.WidgetAccentOf.Remove(id); _settings.Save(); RebuildWidget(id); ShowWidgetEditor(id); };
        accents.Children.Add(auto);
        foreach (var c in Palette)
        {
            string hex = c.ToString().Substring(3);
            bool on = ownAccent == hex;
            var dot = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(on ? 7 : 3.5), BorderBrush = new SolidColorBrush(c),
            };
            dot.MouseLeftButtonUp += (_, e) => { e.Handled = true; _settings.WidgetAccentOf[id] = hex; _settings.Save(); RebuildWidget(id); ShowWidgetEditor(id); };
            accents.Children.Add(dot);
        }
        stack.Children.Add(accents);

        // the widget's own options
        var extra = WidgetOptions(id);
        if (extra != null) { stack.Children.Add(Head(L.T("Параметры"))); stack.Children.Add(extra); }

        // remove, at the bottom, in red
        var remove = WKey(W(L.T("Убрать виджет"), 13.5, FontWeights.SemiBold, Solid(0xFF, 0x6B, 0x60)), Solid(0xFF, 0x45, 0x3A, 0x26), null,
                          () => { self?.Close(); RemoveWidget(id); foreach (var u in _galleryUpdaters) try { u(); } catch { } }, double.NaN, 36, 18, evenWhenEditing: true);
        remove.Margin = new Thickness(0, 18, 0, 0);
        stack.Children.Add(remove);

        var sheet = new Border
        {
            Width = 340, CornerRadius = new CornerRadius(26), Padding = new Thickness(18, 16, 18, 16), Child = stack, BorderThickness = new Thickness(1.2),
            Background = new LinearGradientBrush(Color.FromArgb(PanelGlass ? (byte)0xA6 : (byte)0xF2, 0x18, 0x18, 0x1E), Color.FromArgb(PanelGlass ? (byte)0xC0 : (byte)0xF2, 0x10, 0x10, 0x16), 90),
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops = { new GradientStop(Color.FromArgb(0x90, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0x18, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0x50, 255, 255, 255), 1) },
            },
        };
        TextElement.SetFontFamily(sheet, new FontFamily("Segoe UI Variable Text, Segoe UI"));
        sheet.Measure(new Size(340, double.PositiveInfinity));
        double h = sheet.DesiredSize.Height;

        // beside the widget, on the side with room (a popover comes out of what it belongs to)
        var home = HomeRect(id, target);
        var area = SystemParameters.WorkArea;
        double x = home.Right + 14 + 340 <= area.Right ? home.Right + 14 : home.Left - 14 - 340;
        double y = Math.Clamp(home.Top, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - h - 8));
        var win = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true,
            ResizeMode = ResizeMode.NoResize, Content = GlassPanel(sheet, 26), Title = "Atoll widget editor", Width = 340, Height = h, Left = Math.Max(area.Left + 8, x), Top = y,
        };
        self = win;
        win.SourceInitialized += (_, _) => FollowGlassPanel(win);
        win.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) win.Close(); };
        bool closing = false;
        win.Closing += (_, _) => closing = true;
        win.Deactivated += (_, _) =>
        {
            if (closing || !ReferenceEquals(_widgetEditor, win)) return;
            closing = true;
            Dispatcher.BeginInvoke(() => { if (win.IsLoaded) win.Close(); });   // never from inside the activation change itself
        };
        win.Closed += (_, _) => { if (ReferenceEquals(_widgetEditor, win)) _widgetEditor = null; _settings.Save(); };
        _widgetEditor = win;
        win.Show();
        if (!Settings.ReadOnly) win.Activate();   // demo pictures never take the keyboard
        if (!Motion.Reduced)
        {
            var s = new ScaleTransform(0.94, 0.94); sheet.RenderTransformOrigin = new Point(x > home.Left ? 0 : 1, 0.2); sheet.RenderTransform = s;
            var a = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, a); s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            sheet.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
    }

    /// <summary>Options only some widgets have.</summary>
    private FrameworkElement? WidgetOptions(string id)
    {
        var white = Brushes.White;
        switch (id)
        {
            case "water":
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var value = new TextBlock { Text = L.F("{0} стаканов в день", _settings.WaterGoal), FontSize = 13.5, Foreground = white, VerticalAlignment = VerticalAlignment.Center, Width = 170 };
                void Set(int d) { _settings.WaterGoal = Math.Clamp(_settings.WaterGoal + d, 2, 20); value.Text = L.F("{0} стаканов в день", _settings.WaterGoal); _settings.Save(); TickWidgets(); }
                row.Children.Add(value);
                row.Children.Add(WKey(Glyph("", 11, white), null, null, () => Set(-1), 36, 30, name: L.T("Меньше"), evenWhenEditing: true));
                var plus = WKey(Glyph("", 11, white), null, null, () => Set(1), 36, 30, name: L.T("Больше"), evenWhenEditing: true);
                plus.Margin = new Thickness(6, 0, 0, 0); row.Children.Add(plus);
                return row;
            }
            case "worldclock":
            {
                var wrap = new WrapPanel();
                foreach (var (name, _) in AllCities)
                {
                    bool on = _settings.WorldCities.Contains(name);
                    var chip = new Border
                    {
                        CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                        Background = on ? Solid(0x0A, 0x84, 0xFF) : Solid(0xFF, 0xFF, 0xFF, 0x1E),
                        Child = new TextBlock { Text = L.T(name), FontSize = 12, Foreground = white },
                    };
                    var city = name;
                    chip.MouseLeftButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        if (_settings.WorldCities.Contains(city)) { if (_settings.WorldCities.Count > 1) _settings.WorldCities.Remove(city); }
                        else { _settings.WorldCities.Add(city); while (_settings.WorldCities.Count > 6) _settings.WorldCities.RemoveAt(0); }
                        _settings.Save(); RebuildWidget("worldclock"); ShowWidgetEditor("worldclock");
                    };
                    wrap.Children.Add(chip);
                }
                return new StackPanel { Children = { new TextBlock { Text = L.T("Первые три видны на среднем виджете, до шести — на большом"), FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }, wrap } };
            }
            case "weather":
            {
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 4, 10, 5), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left,
                    Background = _settings.WeatherAtmosphere ? Solid(0x0A, 0x84, 0xFF) : Solid(0xFF, 0xFF, 0xFF, 0x1E),
                    Child = new TextBlock { Text = L.T("Живой фон по погоде"), FontSize = 12, Foreground = white },
                    ToolTip = L.T("Едва заметная дымка, капли, снег или тёплый блик — по погоде за окном"),
                };
                chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; _settings.WeatherAtmosphere = !_settings.WeatherAtmosphere; _settings.Save(); RebuildWidget("weather"); ShowWidgetEditor("weather"); };
                return chip;
            }
            case "photos":
            {
                var dimText = new SolidColorBrush(Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF));
                Border Chip(string label, bool on, Action click)
                {
                    var chip = new Border
                    {
                        CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                        Background = on ? Solid(0x0A, 0x84, 0xFF) : Solid(0xFF, 0xFF, 0xFF, 0x1E),
                        Child = new TextBlock { Text = label, FontSize = 12, Foreground = white },
                    };
                    chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
                    return chip;
                }
                void Changed() { _settings.Save(); _photoListKey = ""; RebuildWidget("photos"); ShowWidgetEditor("photos"); }
                TextBlock Head(string text) => new() { Text = text, FontSize = 12, Foreground = dimText, Margin = new Thickness(0, 8, 0, 6) };
                var panel = new StackPanel();
                panel.Children.Add(Head(L.T("Источник")));
                var modes = new WrapPanel();
                foreach (var (key, label) in new[] { ("folder", L.T("Папка")), ("single", L.T("Одно фото")), ("thisday", L.T("Этот день")) })
                {
                    var k = key;
                    modes.Children.Add(Chip(label, _settings.PhotoMode == key, () => { _settings.PhotoMode = k; Changed(); }));
                }
                panel.Children.Add(modes);
                // pick a folder or a file: the editor closes first (a dialog over it would close it mid-way)
                bool single = _settings.PhotoMode == "single";
                var where = single ? (_settings.PhotoFile.Length > 0 ? System.IO.Path.GetFileName(_settings.PhotoFile) : L.T("фото не выбрано"))
                                   : (_settings.PhotoFolder.Length > 0 ? _settings.PhotoFolder : L.T("Изображения"));
                panel.Children.Add(new TextBlock { Text = where, FontSize = 11.5, Foreground = white, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 6) });
                panel.Children.Add(Chip(single ? L.T("Выбрать фото…") : L.T("Выбрать папку…"), false, () =>
                {
                    _widgetEditor?.Close();
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (single)
                        {
                            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("Фото") + "|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.heic" };
                            if (dlg.ShowDialog() == true) _settings.PhotoFile = dlg.FileName;
                        }
                        else
                        {
                            using var dlg = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = _settings.PhotoFolder.Length > 0 ? _settings.PhotoFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
                            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) _settings.PhotoFolder = dlg.SelectedPath;
                        }
                        Changed();
                    });
                }));
                if (!single)
                {
                    panel.Children.Add(Head(L.T("Менять фото")));
                    var every = new WrapPanel();
                    foreach (var (min, label) in new[] { (1, L.T("каждую минуту")), (5, L.T("каждые 5 мин")), (30, L.T("каждые 30 мин")), (60, L.T("каждый час")), (1440, L.T("раз в день")) })
                    {
                        var m = min;
                        every.Children.Add(Chip(label, _settings.PhotoIntervalMin == min, () => { _settings.PhotoIntervalMin = m; Changed(); }));
                    }
                    panel.Children.Add(every);
                }
                var toggles = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                if (!single)
                {
                    toggles.Children.Add(Chip(L.T("Перемешивать"), _settings.PhotoShuffle, () => { _settings.PhotoShuffle = !_settings.PhotoShuffle; Changed(); }));
                    toggles.Children.Add(Chip(L.T("С подпапками"), _settings.PhotoSubfolders, () => { _settings.PhotoSubfolders = !_settings.PhotoSubfolders; Changed(); }));
                }
                toggles.Children.Add(Chip(L.T("Медленное приближение"), _settings.PhotoKenBurns, () => { _settings.PhotoKenBurns = !_settings.PhotoKenBurns; Changed(); }));
                panel.Children.Add(toggles);
                // the focus point: a click on the picture sets the middle of the framing
                if (CurrentPhoto() is { } photo)
                {
                    panel.Children.Add(Head(L.T("Главное на фото — нажмите туда")));
                    var pic = new Image { Width = 240, Stretch = Stretch.Uniform, Cursor = Cursors.Cross, HorizontalAlignment = HorizontalAlignment.Left };
                    try
                    {
                        var bmp = new System.Windows.Media.Imaging.BitmapImage();
                        bmp.BeginInit(); bmp.UriSource = new Uri(photo); bmp.DecodePixelWidth = 480; bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bmp.EndInit(); bmp.Freeze();
                        pic.Source = bmp;
                    }
                    catch (Exception e) { Log.Write("photos: focus picker: " + e.Message); }
                    var mark = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, Fill = Solid(0x0A, 0x84, 0xFF, 0xA0), IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                    var holder = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Children = { pic, mark } };
                    void Place()
                    {
                        mark.Visibility = _settings.PhotoFocus.TryGetValue(photo, out var f) ? Visibility.Visible : Visibility.Collapsed;
                        if (f is { Length: 2 } && pic.ActualWidth > 0) mark.Margin = new Thickness(f[0] * pic.ActualWidth - 7, f[1] * pic.ActualHeight - 7, 0, 0);
                    }
                    pic.SizeChanged += (_, _) => Place();
                    pic.MouseLeftButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        var p = e.GetPosition(pic);
                        _settings.PhotoFocus[photo] = [Math.Clamp(p.X / pic.ActualWidth, 0, 1), Math.Clamp(p.Y / pic.ActualHeight, 0, 1)];
                        while (_settings.PhotoFocus.Count > 500) _settings.PhotoFocus.Remove(_settings.PhotoFocus.Keys.First());
                        _settings.Save(); Place(); RebuildWidget("photos");
                    };
                    panel.Children.Add(holder);
                    if (_settings.PhotoFocus.ContainsKey(photo))
                    {
                        var auto = Chip(L.T("Авто (по лицам)"), false, () => { _settings.PhotoFocus.Remove(photo); _settings.Save(); RebuildWidget("photos"); ShowWidgetEditor("photos"); });
                        auto.Margin = new Thickness(0, 6, 0, 0); auto.HorizontalAlignment = HorizontalAlignment.Left;
                        panel.Children.Add(auto);
                    }
                }
                return panel;
            }
            case "clock":
            {
                var wrap = new WrapPanel();
                foreach (var (key, label) in new[] { ("classic", L.T("Классика")), ("minimal", L.T("Минимал")), ("digital", L.T("Цифровой")) })
                {
                    bool on = _settings.ClockFace == key;
                    var chip = new Border
                    {
                        CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                        Background = on ? Solid(0x0A, 0x84, 0xFF) : Solid(0xFF, 0xFF, 0xFF, 0x1E),
                        Child = new TextBlock { Text = label, FontSize = 12, Foreground = white },
                    };
                    var k = key;
                    chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; _settings.ClockFace = k; _settings.Save(); RebuildWidget("clock"); ShowWidgetEditor("clock"); };
                    wrap.Children.Add(chip);
                }
                // the sweep costs about 1% CPU per face; jumping once a second is almost free
                var sweep = new Border
                {
                    CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 8, 0, 0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left,
                    Background = _settings.ClockSweep ? Solid(0x0A, 0x84, 0xFF) : Solid(0xFF, 0xFF, 0xFF, 0x1E),
                    Child = new TextBlock { Text = L.T("Плавная секундная стрелка"), FontSize = 12, Foreground = white },
                    ToolTip = L.T("Красивее, но заметно нагружает процессор; выключенная стрелка шагает раз в секунду"),
                };
                sweep.MouseLeftButtonUp += (_, e) => { e.Handled = true; _settings.ClockSweep = !_settings.ClockSweep; _settings.Save(); RebuildWidget("clock"); ShowWidgetEditor("clock"); };
                return new StackPanel { Children = { new TextBlock { Text = L.T("Циферблат"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 0, 0, 8) }, wrap, sweep } };
            }
            case "music":
            {
                var wrap = new WrapPanel();
                foreach (var (key, label) in new[] { ("wave", L.T("Волна")), ("bars", L.T("Столбики")), ("ring", L.T("Кольцо")), ("off", L.T("Без визуализации")) })
                {
                    bool on = _settings.MusicViz == key;
                    var chip = new Border
                    {
                        CornerRadius = new CornerRadius(13), Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                        Background = on ? Solid(0x0A, 0x84, 0xFF) : Solid(0xFF, 0xFF, 0xFF, 0x1E),
                        Child = new TextBlock { Text = label, FontSize = 12, Foreground = white },
                    };
                    var k = key;
                    chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; _settings.MusicViz = k; _settings.Save(); RebuildWidget("music"); ShowWidgetEditor("music"); };
                    wrap.Children.Add(chip);
                }
                // the words' sync shift: some timed lyrics run a little early or late
                var dimText = new SolidColorBrush(Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF));
                var shown = new TextBlock { Text = LyricsOffsetText(), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = white, MinWidth = 86, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                FrameworkElement Step(string glyph, int ms, string name) =>
                    WKey(W(glyph, 15, FontWeights.SemiBold, white), Solid(0xFF, 0xFF, 0xFF, 0x1E), null, () => { ShiftLyrics(ms); shown.Text = LyricsOffsetText(); }, 34, 30, 15, name: name, evenWhenEditing: true);
                var shiftRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { Step("−", -50, L.T("Текст позже")), shown, Step("+", 50, L.T("Текст раньше")) } };
                var reset = WKey(W(L.T("Сбросить"), 12, null, white), Brushes.Transparent, null, () => { ShiftLyrics(0); shown.Text = LyricsOffsetText(); }, double.NaN, 30, 15, evenWhenEditing: true);
                reset.Margin = new Thickness(8, 0, 0, 0);
                shiftRow.Children.Add(reset);
                return new StackPanel { Children =
                {
                    new TextBlock { Text = L.T("Визуализация звука"), FontSize = 12, Foreground = dimText, Margin = new Thickness(0, 0, 0, 8) }, wrap,
                    new TextBlock { Text = L.T("Сдвиг текста песни"), FontSize = 12, Foreground = dimText, Margin = new Thickness(0, 10, 0, 8) }, shiftRow,
                } };
            }
            case "countdown":
            {
                var b = WKey(W(L.T("Изменить событие и дату…"), 13, FontWeights.SemiBold, white), null, null, () => { _widgetEditor?.Close(); ExpandWidget("countdown"); }, double.NaN, 34, 17, evenWhenEditing: true);
                return b;
            }
            case "launcher":
            {
                return WKey(W(L.T("Выбрать приложения в доке острова…"), 13, FontWeights.SemiBold, white), null, null, () => { _widgetEditor?.Close(); OpenTab(TabHome); }, double.NaN, 34, 17, evenWhenEditing: true);
            }
            default: return null;
        }
    }

    // ------------------------------------------------------------------ ready-made sets

    private static readonly (string Name, string About, string[] Ids)[] WidgetSets =
    [
        (L.T("Мой день"), L.T("Время, погода, месяц, дела и музыка"), ["clock", "weather", "month", "todo", "music"]),
        (L.T("Работа"), L.T("Календарь, напоминания, заметки, таймер, фокус"), ["calendar", "reminders", "notes", "timer", "focus", "clipboard"]),
        (L.T("Компьютер"), L.T("Нагрузка, память, сеть и процессы"), ["system", "cpu", "gpu", "ram", "net", "top"]),
        (L.T("Спокойствие"), L.T("Фото, вода, дыхание, луна и солнце"), ["photos", "water", "breathe", "moon", "sun"]),
    ];

    /// <summary>Puts a whole set on the desktop, neatly on the grid, one after another (nothing already there moves).</summary>
    private void AddWidgetSet(string[] ids)
    {
        int i = 0;
        foreach (var id in ids)
        {
            if (_widgets.ContainsKey(id) || !OfferedWidgets.Any(k => k.Id == id)) continue;
            AddWidget(id, pop: false);
            if (_widgets.TryGetValue(id, out var w)) Materialize(w, 70 * i++);
        }
        foreach (var u in _galleryUpdaters) try { u(); } catch { }
    }

    private FrameworkElement WidgetSetCards(bool onIsland)
    {
        var wrap = new WrapPanel();
        foreach (var (name, about, ids) in WidgetSets)
        {
            var icons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var id in ids)
            {
                var k = WidgetKinds.First(x => x.Id == id);
                var dot = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 4, 0), Background = Solid(0xFF, 0xFF, 0xFF, 0x22),
                                       Child = new TextBlock { Text = k.Icon, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 11, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                icons.Children.Add(dot);
            }
            var content = new StackPanel { Margin = new Thickness(14, 12, 14, 12), Children =
            {
                icons,
                new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
                new TextBlock { Text = about, FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF)), TextWrapping = TextWrapping.Wrap },
            } };
            var set = ids;
            var card = WKey(content, Solid(0xFF, 0xFF, 0xFF, 0x14), null, () => { AddWidgetSet(set); if (onIsland) RenderWidgetsPage(); }, onIsland ? 280 : 190, double.NaN, 18, evenWhenEditing: true);
            card.Height = double.NaN; card.Margin = new Thickness(0, 0, 10, 10); card.ToolTip = L.T("Добавить набор на рабочий стол");
            ((FrameworkElement)card.Tag).HorizontalAlignment = HorizontalAlignment.Left;
            wrap.Children.Add(card);
        }
        return wrap;
    }

    /// <summary>The first widget ever: say once how to use them.</summary>
    private void MaybeWidgetTip()
    {
        if (_settings.WidgetTipShown || Settings.ReadOnly) return;
        _settings.WidgetTipShown = true; _settings.Save();
        ShowToast("", L.T("Виджет на рабочем столе"), L.T("Нажмите — раскроется. Правый клик — размер, цвет и настройки."), ms: 6000);
    }
}

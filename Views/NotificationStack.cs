using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// The notifications the island has shown, as a stack of cards (after Kokonut UI's Notification stack, MIT,
/// @dorianbaffier, kokonutui.com): folded, the newest card sits in front with two more peeking out 10 px below
/// it, each a little smaller; hovered (or clicked, or "Show all") it fans out into a list. A card is swiped
/// sideways to dismiss: past 100 px or with a flick it flies off, speeding up; otherwise it springs back. Clicking
/// an open card does what the notification's own click did. Same numbers as the original; frames only while
/// something moves.
/// </summary>
public sealed class NotificationStack : StackPanel
{
    public sealed record Item(string Glyph, Brush? Badge, string Title, string? Body, DateTime At, Action? Open);

    // notification-stack.tsx
    private const double Peek = 10, Gap = 8, FallbackHeight = 72;
    private const int VisibleBehind = 2;
    private const double DismissDistance = 100, DismissVelocity = 0.11, AxisLock = 6;
    private const double SpringK = 520, FlightMin = 1400, FlightAccel = 4200;
    private const double MoveSeconds = .3, FadeSeconds = .2, ExitSeconds = .2;
    private static readonly double SpringC = 2 * Math.Sqrt(SpringK);
    private static readonly CubicBezierEase EaseOut = new(0.23, 1, 0.32, 1);
    public const int MaxCards = 12;

    private sealed class Tween(double v)
    {
        public double From = v, To = v, Now = v, Start = -1, Length;
        public void Go(double to, double length, double clock)
        {
            if (Math.Abs(to - To) < 1e-6 && Start >= 0) return;
            From = Now; To = to; Start = clock; Length = length;
            if (length <= 0) { Now = to; Start = -1; }
        }
        public bool Step(double clock)
        {
            if (Start < 0) return false;
            double t = Length <= 0 ? 1 : Math.Clamp((clock - Start) / Length, 0, 1);
            Now = From + (To - From) * EaseOut.Ease(t);
            if (t >= 1) { Now = To; Start = -1; return false; }
            return true;
        }
    }

    private sealed class Card
    {
        public required Item Item;
        public required Grid Root;
        public required Border Face;
        public required FrameworkElement Content;
        public required Border Close;
        public required TextBlock Time;
        public readonly TranslateTransform Move = new();
        public readonly ScaleTransform Scale = new();
        public readonly Tween Y = new(0), S = new(1), O = new(1), Muted = new(0);
        public double X, V;           // the swipe and its speed (px/s)
        public int Physics;           // 0 none, 1 springing back, 2 flying off
        public int Direction;
        public double Flight;         // the flight's speed
        public double FadeLeft = -1;  // reduced motion: fading out
        public bool Leaving;
    }

    private readonly List<Card> _cards = [];   // newest first
    private readonly Grid _list = new() { ClipToBounds = false };
    private readonly TextBlock _title = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly System.Windows.Documents.Run _count = new();
    private readonly Button _toggle = new() { Padding = new Thickness(10, 3, 10, 3), FontSize = 12, Focusable = false };
    private readonly Button _clear = new() { Padding = new Thickness(10, 3, 10, 3), FontSize = 12, Focusable = false, Margin = new Thickness(0, 0, 4, 0) };
    private bool _hovered, _pinned, _ticking;
    private TimeSpan _lastFrame;
    private double _clock;

    private (Card Card, Point Start, double Origin, char Axis, List<(int T, double X)> Samples)? _drag;
    private bool _swallowClick;

    /// <summary>The stack's height changed (the island refits to it).</summary>
    public event Action? Resized;
    /// <summary>A card left (swiped, closed or opened); its item.</summary>
    public event Action<Item>? Dismissed;

    public int Count => _cards.Count(c => !c.Leaving);
    public bool Expanded => (_hovered || _pinned) && Count > 1;

    public NotificationStack()
    {
        _title.Inlines.Add(new System.Windows.Documents.Run(L.T("Уведомления")));
        _title.Inlines.Add(new System.Windows.Documents.Run("  "));
        _count.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Fg3");
        _title.Inlines.Add(_count);
        _toggle.SetResourceReference(StyleProperty, "GhostPill");
        _toggle.Click += (_, _) => { _pinned = !Expanded; if (!_pinned) _hovered = false; Relayout(); };
        _clear.SetResourceReference(StyleProperty, "GhostPill");
        _clear.Content = L.T("Очистить все");
        _clear.Click += (_, _) => ClearAll();
        var head = new Grid { Height = 28, Margin = new Thickness(4, 0, 2, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(_title);
        Grid.SetColumn(_clear, 1);
        head.Children.Add(_clear);
        Grid.SetColumn(_toggle, 2);
        head.Children.Add(_toggle);
        Children.Add(head);
        Children.Add(_list);
        Visibility = Visibility.Collapsed;
        MouseEnter += (_, _) => { _hovered = true; Relayout(); };
        MouseLeave += (_, _) => { if (_drag != null) return; _hovered = false; Relayout(); };
    }

    /// <summary>A new notification goes on top (the oldest falls off past <see cref="MaxCards"/>).</summary>
    public void Add(Item item)
    {
        var card = MakeCard(item);
        _cards.Insert(0, card);
        _list.Children.Add(card.Root);
        foreach (var old in _cards.Where(c => !c.Leaving).Skip(MaxCards).ToList()) Remove(old, report: false);
        // the newcomer starts where the front card is, so it lands on the stack instead of sliding in from the top
        card.O.Now = card.O.From = card.O.To = 0;
        Visibility = Visibility.Visible;
        Relayout();
    }

    /// <summary>Folds the stack (the island closed) and brings the times up to date.</summary>
    public void Refresh(bool fold)
    {
        if (fold) { _pinned = false; _hovered = false; }
        foreach (var c in _cards) { c.Time.Text = Ago(c.Item.At); Paint(c); }
        Relayout(animate: !fold);
    }

    /// <summary>The user acted on it elsewhere (clicked the island's notice): it is not missed any more.</summary>
    public void Forget(Item item)
    {
        if (_cards.FirstOrDefault(c => c.Item == item) is { } c) Remove(c, report: false);
    }

    public void Clear()
    {
        foreach (var c in _cards.ToList()) Remove(c, report: false);
        Relayout();
    }

    /// <summary>"Clear all": every card goes as if dismissed one by one, the stack shrinks once.</summary>
    public void ClearAll()
    {
        var gone = _cards.ToList();
        _cards.Clear();
        _list.Children.Clear();
        _pinned = false;
        foreach (var c in gone) Dismissed?.Invoke(c.Item);
        Relayout();
    }

    /// <summary>
    /// The cursor is on the stack: it does not get shorter now. Every shrink resized the island under the cursor,
    /// which then found itself outside, and the island folded (closing cards one after another). It shrinks once
    /// the cursor has left.
    /// </summary>
    private bool Held => _hovered || IsMouseOver || _drag != null;

    private static string Ago(DateTime at)
    {
        var d = DateTime.Now - at;
        if (d.TotalMinutes < 1) return L.T("сейчас");
        if (d.TotalMinutes < 60) return L.F("{0} мин", (int)d.TotalMinutes);
        return at.Date == DateTime.Today ? at.ToString("HH:mm") : at.ToString("dd.MM HH:mm");
    }

    private Card MakeCard(Item item)
    {
        var icon = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top };
        var glyph = new TextBlock { Text = item.Glyph, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // Fluent glyphs live in the private-use area; Claude's ✳ and Codex's ◎ are ordinary symbols the icon font lacks
        if (item.Glyph.Length > 0 && item.Glyph[0] >= '' && item.Glyph[0] <= '') glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        if (item.Badge != null) { icon.Background = item.Badge; glyph.Foreground = Brushes.White; }
        else { icon.SetResourceReference(Border.BackgroundProperty, "ChipBg"); glyph.SetResourceReference(TextBlock.ForegroundProperty, "Fg"); }
        icon.Child = glyph;

        var title = new TextBlock { Text = item.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var time = new TextBlock { Text = Ago(item.At), FontSize = 11, Margin = new Thickness(8, 1, 0, 0) };
        time.SetResourceReference(TextBlock.ForegroundProperty, "Fg3");
        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(time, Dock.Right);
        top.Children.Add(time);
        top.Children.Add(title);
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { top } };
        if (!string.IsNullOrWhiteSpace(item.Body))
        {
            var body = new TextBlock { Text = item.Body, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 34, Margin = new Thickness(0, 2, 0, 0) };
            body.SetResourceReference(TextBlock.ForegroundProperty, "Fg2");
            text.Children.Add(body);
        }
        var content = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        content.Children.Add(icon);
        content.Children.Add(text);

        // the face is solid (the island's own colour under the tile's tint): the cards behind must not show through
        var tint = new Border { Padding = new Thickness(12, 11, 12, 11), CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), Child = content };
        tint.SetResourceReference(Border.BackgroundProperty, "TileBg");
        tint.SetResourceReference(Border.BorderBrushProperty, "TileStroke");
        var face = new Border { CornerRadius = new CornerRadius(22), Child = tint };

        var x = new TextBlock { Text = "", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        x.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        x.SetResourceReference(TextBlock.ForegroundProperty, "Fg2");
        var close = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), Child = x, Cursor = Cursors.Hand,
                                 HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-6, -6, 0, 0), Opacity = 0,
                                 ToolTip = L.T("Убрать") };
        close.SetResourceReference(Border.BorderBrushProperty, "TileStroke");

        var root = new Grid { VerticalAlignment = VerticalAlignment.Top, RenderTransformOrigin = new Point(.5, 0), Children = { face, close } };
        var card = new Card { Item = item, Root = root, Face = face, Content = content, Close = close, Time = time };
        root.RenderTransform = new TransformGroup { Children = { card.Scale, card.Move } };
        Paint(card);

        root.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > .5) Relayout(); };
        root.MouseEnter += (_, _) => Fade(close, IsMuted(card) ? 0 : 1);
        root.MouseLeave += (_, _) => Fade(close, 0);
        close.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (!IsMuted(card)) Dismiss(card, 1, 0); };
        close.MouseLeftButtonDown += (_, e) => e.Handled = true;
        root.MouseLeftButtonDown += (_, e) => Down(card, e);
        root.MouseMove += (_, e) => MoveDrag(card, e);
        root.MouseLeftButtonUp += (_, e) => Up(card, e);
        root.LostMouseCapture += (_, _) => { if (_drag?.Card == card && _drag.Value.Axis == 'x') Release(card); };
        return card;
    }

    private void Paint(Card card)
    {
        var c = (TryFindResource("IslandBg") as SolidColorBrush)?.Color ?? Colors.Black;
        card.Face.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        card.Close.Background = card.Face.Background;
    }

    private static void Fade(UIElement e, double to) =>
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(Motion.Reduced ? 0 : 150)));

    private bool IsMuted(Card card) => !Expanded && _cards.Where(c => !c.Leaving).ToList().IndexOf(card) > 0;

    private double HeightOf(Card c) => c.Root.ActualHeight > 0 ? c.Root.ActualHeight : FallbackHeight;

    /// <summary>Every card to its place: folded (bottom edges 10 px apart, 5 % smaller each) or fanned out.</summary>
    private void Relayout(bool animate = true)
    {
        var laid = _cards.Where(c => !c.Leaving).ToList();
        bool open = Expanded;
        double move = animate && !Motion.Reduced ? MoveSeconds : 0, fade = animate && !Motion.Reduced ? FadeSeconds : 0;
        double front = laid.Count > 0 ? HeightOf(laid[0]) : 0;
        double height = open ? laid.Sum(HeightOf) + Math.Max(laid.Count - 1, 0) * Gap
                             : front + Math.Min(Math.Max(laid.Count - 1, 0), VisibleBehind) * Peek;
        double running = 0;
        for (int i = 0; i < laid.Count; i++)
        {
            var c = laid[i];
            double scale = open ? 1 : 1 - i * 0.05;
            double y = open ? running : front + i * Peek - HeightOf(c) * scale;
            running += HeightOf(c) + Gap;
            bool hidden = !open && i > VisibleBehind;
            bool fresh = c.Y.Start < 0 && c.O.To == 0 && c.O.Now == 0;
            if (fresh) { c.Y.Now = c.Y.From = c.Y.To = y; c.S.Now = c.S.From = c.S.To = scale; }
            c.Y.Go(y, move, _clock);
            c.S.Go(scale, move, _clock);
            c.O.Go(hidden ? 0 : 1, fresh ? fade : move, _clock);
            c.Muted.Go(!open && i > 0 ? 1 : 0, fade, _clock);
            c.Root.IsHitTestVisible = !hidden;
            c.Root.Cursor = !open && i == 0 && laid.Count > 1 ? Cursors.Hand : null;
            Panel.SetZIndex(c.Root, _cards.Count - i);
            if (c.Close.Opacity > 0 && !open && i > 0) Fade(c.Close, 0);
        }
        foreach (var c in _cards.Where(c => c.Leaving)) { c.S.Go(1, move, _clock); Panel.SetZIndex(c.Root, _cards.Count + 1); }

        _count.Text = laid.Count.ToString();
        _toggle.Visibility = laid.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _toggle.Content = open ? L.T("Свернуть") : L.T("Показать все");
        _clear.Visibility = laid.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool held = Held && !double.IsNaN(_list.Height) && height < _list.Height - .5;
        if (held) height = _list.Height;
        bool gone = _cards.Count == 0 && !held && Visibility == Visibility.Visible;
        if (gone) Visibility = Visibility.Collapsed;
        if (Math.Abs(_list.Height - height) > .5 || double.IsNaN(_list.Height) || gone)
        {
            _list.Height = height;
            Resized?.Invoke();
        }
        Apply();
        Start();
    }

    private void Apply()
    {
        foreach (var c in _cards)
        {
            c.Move.Y = c.Y.Now;
            c.Move.X = c.X;
            c.Scale.ScaleX = c.Scale.ScaleY = c.S.Now;
            double swipe = c.Physics == 2 ? Math.Max(1 - Math.Abs(c.X) / (c.Root.ActualWidth * 1.1), 0)
                         : c.X == 0 ? 1 : Math.Max(1 - Math.Abs(c.X) / 240, 0.3);
            double fadeOut = c.FadeLeft >= 0 ? Math.Clamp(c.FadeLeft / ExitSeconds, 0, 1) : 1;
            c.Root.Opacity = c.O.Now * swipe * fadeOut;
            c.Content.Opacity = 1 - c.Muted.Now;
        }
    }

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
        _clock += dt;
        bool moving = false;
        foreach (var c in _cards.ToList())
        {
            moving |= c.Y.Step(_clock) | c.S.Step(_clock) | c.O.Step(_clock) | c.Muted.Step(_clock);
            if (c.Physics == 1)
            {
                // critically damped: carries the release a little further, never wobbles past rest
                for (int i = 0; i < 4; i++) { double a = -SpringK * c.X - SpringC * c.V; c.V += a * dt / 4; c.X += c.V * dt / 4; }
                if (Math.Abs(c.X) < 0.5 && Math.Abs(c.V) < 10) { c.X = 0; c.V = 0; c.Physics = 0; }
                else moving = true;
            }
            else if (c.Physics == 2)
            {
                double distance = Math.Max(c.Root.ActualWidth, 200) * 1.1;
                c.Flight += FlightAccel * dt;
                c.X += c.Direction * c.Flight * dt;
                if (Math.Abs(c.X) >= distance) { c.X = c.Direction * distance; c.Physics = 0; Remove(c, report: true); continue; }
                moving = true;
            }
            if (c.FadeLeft >= 0)
            {
                c.FadeLeft -= dt;
                if (c.FadeLeft <= 0) { Remove(c, report: true); continue; }
                moving = true;
            }
        }
        Apply();
        if (!moving && _drag == null)
        {
            _ticking = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    private void Remove(Card c, bool report)
    {
        _cards.Remove(c);
        _list.Children.Remove(c.Root);
        if (report) Dismissed?.Invoke(c.Item);
        if (_cards.Count == 0) _pinned = false;
        Relayout();
    }

    /// <summary>Throws the card off the side; a swipe keeps the speed it was let go at.</summary>
    private void Dismiss(Card c, int direction, double velocity)
    {
        if (c.Leaving) return;
        c.Leaving = true;
        Fade(c.Close, 0);
        if (Motion.Reduced) c.FadeLeft = ExitSeconds;
        else { c.Physics = 2; c.Direction = direction; c.Flight = Math.Max(Math.Abs(velocity) * 1000, FlightMin); }
        Relayout();
    }

    private void Down(Card c, MouseButtonEventArgs e)
    {
        if (_drag != null || c.Leaving) return;
        var laid = _cards.Where(k => !k.Leaving).ToList();
        if (!Expanded && laid.IndexOf(c) > 0) return;
        _swallowClick = false;
        c.Physics = 0;   // catch a card still springing back
        _drag = (c, e.GetPosition(this), c.X, ' ', []);
    }

    private void MoveDrag(Card c, MouseEventArgs e)
    {
        if (_drag is not { } d || d.Card != c) return;
        if (e.LeftButton != MouseButtonState.Pressed) { Up(c, null); return; }
        var p = e.GetPosition(this);
        double dx = p.X - d.Start.X, dy = p.Y - d.Start.Y;
        if (d.Axis == ' ')
        {
            if (Math.Abs(dx) < AxisLock && Math.Abs(dy) < AxisLock) return;
            d.Axis = Math.Abs(dx) > Math.Abs(dy) ? 'x' : 'y';
            _drag = d;
            if (d.Axis == 'x') { c.Root.CaptureMouse(); Start(); }
        }
        if (d.Axis != 'x') return;
        c.X = d.Origin + dx;
        d.Samples.Add((e.Timestamp, c.X));
        if (d.Samples.Count > 12) d.Samples.RemoveAt(0);
        Apply();
    }

    private void Up(Card c, MouseButtonEventArgs? e)
    {
        if (_drag is not { } d || d.Card != c) return;
        if (d.Axis == 'x') { Release(c); return; }
        _drag = null;
        if (d.Axis == 'y' || e == null) return;
        // a plain click: folded, it fans the stack out; open, it does what the notification did
        if (!Expanded) { _pinned = true; Relayout(); return; }
        if (_swallowClick) return;
        Dismiss(c, 1, 0);
        try { c.Item.Open?.Invoke(); }
        catch (Exception ex) { Log.Write("notification stack: open: " + ex.Message); }
    }

    private void Release(Card c)
    {
        if (_drag is not { } d || d.Card != c) return;
        _drag = null;
        if (c.Root.IsMouseCaptured) c.Root.ReleaseMouseCapture();
        _swallowClick = true;
        double velocity = Velocity(d.Samples);   // px/ms
        bool flick = Math.Abs(velocity) > DismissVelocity && Math.Sign(velocity) == Math.Sign(c.X);
        if (Math.Abs(c.X) < DismissDistance && !flick)
        {
            c.V = velocity * 1000;
            if (Motion.Reduced) { c.X = 0; c.V = 0; } else c.Physics = 1;
            Start();
            return;
        }
        int dir = (c.X == 0 ? velocity : c.X) > 0 ? 1 : -1;
        Dismiss(c, dir, Math.Sign(velocity) == dir ? velocity : 0);
    }

    /// <summary>From the last 80 ms of the drag, not the average.</summary>
    private static double Velocity(List<(int T, double X)> s)
    {
        if (s.Count == 0) return 0;
        var last = s[^1];
        var first = last;
        for (int i = s.Count - 1; i >= 0; i--) { if (last.T - s[i].T > 80) break; first = s[i]; }
        int dt = last.T - first.T;
        return dt > 0 ? (last.X - first.X) / dt : 0;
    }

    /// <summary>Tests: each card's place, scale and opacity, the stack's height; and a swipe without a mouse.</summary>
    internal (double ListHeight, bool Open, (double Y, double Scale, double Opacity, double X, bool Leaving)[] Cards) TestState =>
        (_list.Height, Expanded, _cards.Select(c => (c.Y.Now, c.S.Now, c.Root.Opacity, c.X, c.Leaving)).ToArray());

    internal void TestHover(bool on) { _hovered = on; Relayout(); }

    /// <summary>Tests: the close button of a card, as if clicked.</summary>
    internal void TestClose(int index) => Dismiss(_cards.Where(k => !k.Leaving).ElementAt(index), 1, 0);


    internal void TestSwipe(int index, double dx, double velocityPxPerMs)
    {
        var c = _cards.Where(k => !k.Leaving).ElementAt(index);
        c.X = dx;
        int now = Environment.TickCount;
        _drag = (c, new Point(), 0, 'x', [(now - 40, dx - velocityPxPerMs * 40), (now, dx)]);
        Release(c);
    }
}

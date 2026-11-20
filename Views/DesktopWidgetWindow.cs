using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// What the glass of a widget is made of. Image: the blurred desktop picture with a refracting rim (the window is
/// see-through, its corners are cut by WPF, it casts a soft shadow). Live: Windows blurs whatever is behind,
/// live (for animated wallpapers, whose picture can't be known). Solid: no see-through at all (transparency
/// turned off in Windows, or Lite mode).
/// </summary>
public enum GlassKind { Image, Live, Solid }

/// <summary>The layers of one widget card (on the desktop or as a preview in the gallery).</summary>
public sealed class WidgetCard
{
    public const double Radius = 26;
    /// <summary>How far beyond the card the desktop picture reaches, for the lens to pull in at the rim.</summary>
    public const double Band = 18;

    public Border Card { get; }
    public Grid Body { get; }
    /// <summary>The corner radius of this card (Windows' own, smaller one when Windows rounds the window).</summary>
    public double CornerRadius { get; private set; } = Radius;
    private readonly Grid _layers;
    private readonly Border _tint, _scrim, _hover, _edge, _sheen, _rim;
    private readonly Rectangle _backdrop;
    private readonly Ellipse _light;
    private readonly Canvas _lightHost;
    private readonly TranslateTransform _lightAt = new();
    private readonly GlassEffect? _glass;
    private WidgetTheme _theme = WidgetThemes.All[0];
    private GlassKind _kind = GlassKind.Solid;

    /// <summary>width/height NaN: the card fills its window (desktop); a number: a fixed-size preview.</summary>
    public WidgetCard(double width = double.NaN, double height = double.NaN)
    {
        _backdrop = new Rectangle { Margin = new Thickness(-Band), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        // the picture is a third of the screen's size: drawn larger, it must be smoothed, not stepped
        RenderOptions.SetBitmapScalingMode(_backdrop, BitmapScalingMode.Linear);
        _glass = GlassEffect.TryCreate();
        _backdrop.Effect = _glass;
        // the glass is redrawn only when it moves: something animating on the card (a visualiser) doesn't make the
        // shader run again every frame
        _backdrop.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
        // keeps the text readable whatever the wallpaper: darker over light pictures, lighter over dark ones
        _scrim = new Border { IsHitTestVisible = false };
        _tint = new Border();
        _hover = new Border { Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
        Body = new Grid();   // holds the views (each with its own colour wash and content)
        _edge = new Border { IsHitTestVisible = false };
        _sheen = new Border { IsHitTestVisible = false };
        _light = new Ellipse { Width = 280, Height = 280, IsHitTestVisible = false, RenderTransform = _lightAt,
                               Fill = new RadialGradientBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)) };
        _lightHost = new Canvas { IsHitTestVisible = false, Opacity = 0 };
        _lightHost.Children.Add(_light);
        _rim = new Border { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(1), IsHitTestVisible = false };

        _layers = new Grid();
        // the scrim goes over the theme's tint: whatever the tint adds, the text keeps its contrast
        foreach (var l in new UIElement[] { _backdrop, _tint, _scrim, _hover, Body, _edge, _sheen, _lightHost, _rim }) _layers.Children.Add(l);
        // ClipToBounds would cut a rectangle: the card is cut to its rounded shape instead
        _layers.SizeChanged += (_, e) =>
        {
            _layers.Clip = new RectangleGeometry(new Rect(e.NewSize), CornerRadius, CornerRadius);
            if (_glass != null) _glass.Geometry = new Point4D(e.NewSize.Width + 2 * Band, e.NewSize.Height + 2 * Band, Band, Radius);
        };

        Card = new Border { Width = width, Height = height, CornerRadius = new CornerRadius(Radius), Child = _layers, Background = Brushes.Transparent };
        TextElement.SetFontFamily(Card, new FontFamily("Segoe UI Variable Text, Segoe UI"));
        Card.SetResourceReference(TextElement.ForegroundProperty, "WFg");

        Card.MouseEnter += (_, e) => Hover(true, e.GetPosition(_layers));
        Card.MouseLeave += (_, _) => Hover(false, null);
        Card.MouseMove += (_, e) => Hover(true, e.GetPosition(_layers));
        // a press lights the glass up a little, like touching it
        Card.PreviewMouseLeftButtonDown += (_, _) => Flash(true);
        Card.PreviewMouseLeftButtonUp += (_, _) => Flash(false);
        Apply(_theme, GlassKind.Solid);
    }

    public WidgetTheme Theme => _theme;

    /// <summary>
    /// No shadow under the text: for cards that animate (a shadow effect is redrawn with everything under it each
    /// frame) and whose own dark colour already carries white text.
    /// </summary>
    public bool NoTextShadow { get; set; }
    public GlassKind Kind => _kind;

    /// <summary>A different corner radius (the clip, the rim, the lens follow).</summary>
    public void SetCornerRadius(double r)
    {
        CornerRadius = r;
        Card.CornerRadius = _rim.CornerRadius = new System.Windows.CornerRadius(r);
        var size = new Size(_layers.ActualWidth, _layers.ActualHeight);
        if (size.Width > 0) _layers.Clip = new RectangleGeometry(new Rect(size), r, r);
    }

    /// <summary>Hover changes (for the window's shadow).</summary>
    public event Action<bool>? Hovered;

    private bool _hovering;

    /// <summary>
    /// The pointer over the glass: it brightens a touch, a soft glint follows the pointer and the rim's light
    /// turns towards it (with inertia). Also used by tests to show the hover look without moving the mouse.
    /// </summary>
    public void Hover(bool on, Point? at)
    {
        if (on != _hovering)
        {
            _hovering = on;
            var d = TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 200);
            _hover.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(on ? 0.05 : 0, d));
            _lightHost.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : on ? 220 : 420)));
            Hovered?.Invoke(on);
        }
        if (at is { } p)
        {
            _lightAt.X = p.X - 140; _lightAt.Y = p.Y - 140;
            var c = new Point(_layers.ActualWidth / 2, _layers.ActualHeight / 2);
            var v = p - c;
            if (v.Length > 1) { v.Normalize(); LightTo(v); }
        }
        else if (!on) LightTo(DefaultLight);
    }

    private static readonly Vector DefaultLight = new(-0.45, -0.89);
    private Vector _lightDir = DefaultLight, _lightTarget = DefaultLight;
    private bool _lightMoving;

    private void LightTo(Vector target)
    {
        _lightTarget = target;
        if (_glass == null) return;
        if (Motion.Reduced) { _lightDir = target; PushLight(); return; }
        if (!_lightMoving) { _lightMoving = true; CompositionTarget.Rendering += StepLight; }
    }

    // the light turns towards the pointer with inertia; nothing runs once it got there
    private void StepLight(object? sender, EventArgs e)
    {
        _lightDir += (_lightTarget - _lightDir) * 0.12;
        if ((_lightTarget - _lightDir).Length < 0.01) { _lightDir = _lightTarget; _lightMoving = false; CompositionTarget.Rendering -= StepLight; }
        PushLight();
    }

    private void PushLight()
    {
        if (_glass == null) return;
        var l = _glass.Light;
        _glass.Light = new Point4D(_lightDir.X, _lightDir.Y, l.Z, 0);
    }

    /// <summary>The press is over (also after a drag, which swallows the button's release).</summary>
    public void Unpress() => Flash(false);

    private void Flash(bool down)
    {
        var a = new DoubleAnimation(down ? 0.12 : _hovering ? 0.05 : 0, TimeSpan.FromMilliseconds(down ? 90 : 360)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        _hover.BeginAnimation(UIElement.OpacityProperty, a);
    }

    /// <summary>strength 0..1 (0.5 = the theme as designed): how deep the tint of the glass is.</summary>
    public void Apply(WidgetTheme t, GlassKind kind, double strength = 0.5)
    {
        _theme = t; _kind = kind;
        bool seeThrough = kind != GlassKind.Solid;
        double f = 0.3 + Math.Clamp(strength, 0, 1) * 1.4;
        static Color Mul(Color c, double f) => Color.FromArgb((byte)Math.Clamp(c.A * f, 6, 250), c.R, c.G, c.B);
        _tint.Background = seeThrough ? new LinearGradientBrush(Mul(t.A, f), Mul(t.B, f), 90) : new LinearGradientBrush(Opaque(t.Fallback), Opaque(Mix(t.Fallback, t.B)), 90);
        _backdrop.Visibility = kind == GlassKind.Image ? Visibility.Visible : Visibility.Collapsed;
        if (kind != GlassKind.Image) _scrim.Background = null;
        var r = Card.Resources;
        r["WFg"] = new SolidColorBrush(t.Fg);
        r["WDim"] = new SolidColorBrush(t.Dim);
        r["WFill"] = new SolidColorBrush(t.Fill);
        r["WTrack"] = new SolidColorBrush(t.Track);
        byte edge = (byte)(kind == GlassKind.Image ? 0x10 : t.LightText ? 0x26 : 0x50);   // the shader already bends the rim: only a hint here
        _edge.Background = new RadialGradientBrush
        {
            RadiusX = 0.72, RadiusY = 0.72,
            GradientStops = { new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.62), new GradientStop(Color.FromArgb(edge, 255, 255, 255), 1.0) },
        };
        _sheen.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops = { new GradientStop(Color.FromArgb(0x22, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.42) },
        };
        byte rimTop = (byte)(kind == GlassKind.Image ? 0x70 : 0xB4);
        _rim.BorderBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(rimTop, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0x22, 255, 255, 255), 0.4),
                new GradientStop(Color.FromArgb(0x12, 255, 255, 255), 0.62), new GradientStop(Color.FromArgb(0x60, 255, 255, 255), 1),
            },
        };
        // white text on glass gets a barely-there shadow
        Body.Effect = t.LightText && seeThrough && !NoTextShadow ? new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.3, RenderingBias = RenderingBias.Performance } : null;
    }

    private static Color Opaque(Color c) => Color.FromRgb(c.R, c.G, c.B);
    private static Color Mix(Color a, Color b) => Color.FromArgb(a.A, (byte)((a.R * 3 + b.R) / 4), (byte)((a.G * 3 + b.G) / 4), (byte)((a.B * 3 + b.B) / 4));

    /// <summary>
    /// The piece of the blurred desktop picture behind the card (source pixels, already widened by the band),
    /// how strongly the rim bends it, and the scrim that keeps text readable.
    /// </summary>
    public void SetBackdrop(BitmapSource? picture, BitmapSource? clear, Rect sourcePx, double refraction, Color scrim)
    {
        if (picture == null || _kind != GlassKind.Image) { _backdrop.Fill = null; return; }
        if (_backdrop.Fill is ImageBrush b && ReferenceEquals(b.ImageSource, picture)) b.Viewbox = sourcePx;
        else _backdrop.Fill = new ImageBrush(picture) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = sourcePx, Stretch = Stretch.Fill };
        if (_glass != null)
        {
            if (_glass.Clear is ImageBrush cb && ReferenceEquals(cb.ImageSource, clear)) cb.Viewbox = sourcePx;
            else if (clear != null) _glass.Clear = new ImageBrush(clear) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = sourcePx, Stretch = Stretch.Fill };
            double k = Math.Clamp(refraction, 0, 1);
            _glass.Lens = new Point4D(10 + 8 * k, 14 * k, 1.6 * k, 1.3);
            _glass.Light = new Point4D(_lightDir.X, _lightDir.Y, 0.62, 0);
        }
        // a little lighter at the top, deeper at the bottom: reads as tinted glass, not a flat grey plate
        _scrim.Background = scrim.A == 0 ? null : new LinearGradientBrush(
            Color.FromArgb((byte)(scrim.A * 0.94), scrim.R, scrim.G, scrim.B), Color.FromArgb((byte)Math.Min(240, scrim.A * 1.08), scrim.R, scrim.G, scrim.B), 90);
    }

    /// <summary>Edit mode: the rim glows and breathes.</summary>
    public void Glow(bool on)
    {
        if (on && !Motion.Reduced)
            _rim.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        else { _rim.BeginAnimation(UIElement.OpacityProperty, null); _rim.Opacity = 1; }
        _rim.BorderThickness = new Thickness(on ? 2 : 1);
    }
}

/// <summary>
/// One desktop widget. It lives on the desktop, under every window; in edit mode it comes forward and can be
/// dragged; opened ("expanded") it grows on a spring into a bigger interactive card, comes to the front and takes
/// the keyboard until you click away. With Image or Solid glass the window is see-through and larger than the
/// card by <see cref="Pad"/> on every side, for the shadow: position and size of the widget are the card's
/// (<see cref="CardX"/> and friends), not the window's.
/// </summary>
public sealed class DesktopWidgetWindow : Window
{
    public string Id { get; }
    public WidgetCard Card { get; }
    /// <summary>The content of the card, swapped between the small and the expanded view.</summary>
    public Grid Host => Card.Body;
    public WindowSpring Spring { get; }
    public GlassKind Kind { get; }
    public double Pad { get; }

    private readonly Border _remove, _shadowShape, _corner;
    private readonly System.Windows.Threading.DispatcherTimer _altWatch = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private DateTime _altSince;
    private readonly DropShadowEffect? _shadow;
    private IntPtr _hwnd;
    private bool _edit, _raised;

    public event Action<DesktopWidgetWindow>? Moved;
    /// <summary>A drag began (from anywhere on the card, any time it isn't open), and the window moved during it.</summary>
    public event Action<DesktopWidgetWindow>? DragStarted;
    public event Action<DesktopWidgetWindow>? Dragging;
    /// <summary>A click (no drag) in edit mode: edit this widget.</summary>
    public event Action<DesktopWidgetWindow>? EditClicked;
    public event Action<DesktopWidgetWindow>? RemoveClicked;
    /// <summary>A click on the card (not on a button inside it), outside edit mode.</summary>
    public event Action<DesktopWidgetWindow>? Clicked;
    /// <summary>It lost the keyboard while open (you clicked somewhere else).</summary>
    public event Action<DesktopWidgetWindow>? ClickedAway;
    /// <summary>The card moved or changed size (the glass behind it must follow).</summary>
    public event Action<DesktopWidgetWindow>? BoundsChanged;
    /// <summary>Alt held down over the widget for a moment: edit the widgets.</summary>
    public event Action<DesktopWidgetWindow>? AltHeld;
    /// <summary>The corner handle is dragged (pointer on screen, DIPs), and let go.</summary>
    public event Action<DesktopWidgetWindow, Point>? Resizing;
    public event Action<DesktopWidgetWindow>? Resized;

    public DesktopWidgetWindow(string id, double width, double height, GlassKind kind)
    {
        Id = id;
        Kind = kind;
        Pad = kind == GlassKind.Live ? 0 : 36;
        WindowStyle = WindowStyle.None;
        // Live glass needs a normal window (Windows blurs behind it, cut by a region); the others are see-through
        // windows whose corners WPF cuts itself — nothing square can show at the corners
        AllowsTransparency = kind != GlassKind.Live;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        UseLayoutRounding = true;
        Width = width + 2 * Pad; Height = height + 2 * Pad;
        Title = "Atoll widget";
        Spring = new WindowSpring(this, Pad);

        Card = new WidgetCard();
        // Windows rounds a live-blur window with its own radius (8 px at 100 %): the card matches it exactly
        if (kind == GlassKind.Live) Card.SetCornerRadius(8);
        var minus = new Border { Width = 10, Height = 2.2, CornerRadius = new CornerRadius(1), Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _remove = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Child = minus, Cursor = Cursors.Hand, Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xEA)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(7),
        };
        _remove.MouseLeftButtonUp += (_, e) => { e.Handled = true; RemoveClicked?.Invoke(this); };

        // the resize corner (edit mode): an arc in the bottom-right corner, dragged to the size wanted
        var arc = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 2,16 A 14,14 0 0 0 16,2"), Stroke = Brushes.White, StrokeThickness = 3.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.6 },
        };
        _corner = new Border
        {
            Width = 30, Height = 30, Background = Brushes.Transparent, Child = arc, Cursor = Cursors.SizeNWSE, Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, Pad + 4, Pad + 4),
            ToolTip = L.T("Потяните, чтобы сменить размер"),
        };
        _corner.MouseLeftButtonDown += (_, e) => { e.Handled = true; _corner.CaptureMouse(); };
        _corner.MouseMove += (_, e) =>
        {
            if (!_corner.IsMouseCaptured) return;
            var p = PointToScreen(e.GetPosition(this));
            var dip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.Transform(p) ?? p;
            Resizing?.Invoke(this, dip);
        };
        _corner.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (_corner.IsMouseCaptured) { _corner.ReleaseMouseCapture(); Resized?.Invoke(this); } };

        // hold Alt over a widget: edit mode (checked only while the pointer is over it)
        _altWatch.Tick += (_, _) =>
        {
            if (!Native.KeyDown(0x12)) { _altSince = default; return; }   // VK_MENU
            if (_altSince == default) _altSince = DateTime.Now;
            else if ((DateTime.Now - _altSince).TotalMilliseconds > 400 && !_edit) { _altSince = default; _altWatch.Stop(); AltHeld?.Invoke(this); }
        };

        // the shadow is cast by its own shape behind the card (an effect on the card would flatten the glass into
        // one bitmap); the shape is a little smaller than the card so no dark edge can show around it
        _shadowShape = new Border { CornerRadius = new CornerRadius(WidgetCard.Radius), Margin = new Thickness(Pad + 3), Background = Brushes.Black, IsHitTestVisible = false };
        if (kind != GlassKind.Live)
        {
            _shadow = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 7, Direction = 270, Opacity = 0.2, RenderingBias = RenderingBias.Performance };
            _shadowShape.Effect = _shadow;
        }
        else _shadowShape.Visibility = Visibility.Collapsed;
        Card.Card.Margin = new Thickness(Pad);
        _remove.Margin = new Thickness(Pad + 7);

        var root = new Grid();
        root.Children.Add(_shadowShape);
        root.Children.Add(Card.Card);
        root.Children.Add(_remove);
        root.Children.Add(_corner);
        Content = root;
        Card.Card.MouseEnter += (_, _) => { _altSince = default; _altWatch.Start(); };
        Card.Card.MouseLeave += (_, _) => _altWatch.Stop();

        Card.Hovered += on =>
        {
            if (_shadow == null) return;
            var d = TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 220);
            _shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(on ? 0.3 : 0.2, d));
            _shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(on ? 36 : 30, d));
        };

        // press: in edit mode it moves at once; otherwise a press that travels a few pixels becomes a drag and a
        // press that doesn't is a click (controls inside the card handle their own presses)
        Card.Card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled || e.ButtonState != MouseButtonState.Pressed || _raised) return;
            _down = e.GetPosition(this);
            if (_edit) { e.Handled = true; Drag(); }
        };
        Card.Card.MouseMove += (_, e) =>
        {
            if (_down is not { } d || _drag || _raised || e.LeftButton != MouseButtonState.Pressed) return;
            if ((e.GetPosition(this) - d).Length > 6) Drag();
        };
        Card.Card.MouseLeftButtonUp += (_, e) =>
        {
            bool click = _down != null && !_drag;
            _down = null;
            if (!_edit && !e.Handled && click) Clicked?.Invoke(this);
        };
        LocationChanged += (_, _) => { BoundsChanged?.Invoke(this); if (_drag) Dragging?.Invoke(this); };
        SizeChanged += (_, _) => BoundsChanged?.Invoke(this);
        Deactivated += (_, _) => { if (_raised) ClickedAway?.Invoke(this); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && _raised) { e.Handled = true; ClickedAway?.Invoke(this); } };
        Closed += (_, _) => { Spring.Stop(); _altWatch.Stop(); };
        // live glass: the region follows every frame of a spring, before the window takes the new size, so the
        // window never shows corners the region doesn't cut
        Spring.Resizing += r => { if (Kind == GlassKind.Live) LiveGlass.Round(this, WidgetCard.Radius, r.Width, r.Height); };
        Loaded += (_, _) => { if (Kind == GlassKind.Live) LiveGlass.Round(this, WidgetCard.Radius); };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // a tool window that never takes the focus: no taskbar button, no Alt+Tab entry
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | 0x08000000);
            if (Kind == GlassKind.Live)
            {
                LiveGlass.Prepare(this, roundCorners: true);
                LiveGlass.Blur(_hwnd, true);
            }
            _owned = LiveGlass.OwnByDesktop(_hwnd);
            HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
            SendToBottom();
        };
    }

    public IntPtr Handle => _hwnd;

    /// <summary>The card's place and size on screen (DIPs) — what the grid, the glass and the settings mean by "the widget".</summary>
    public double CardX { get => Left + Pad; set => Left = value - Pad; }
    public double CardY { get => Top + Pad; set => Top = value - Pad; }
    public double CardW => Width - 2 * Pad;
    public double CardH => Height - 2 * Pad;
    public Rect CardRect => new(CardX, CardY, CardW, CardH);

    private Point? _down;
    private bool _drag;

    private void Drag()
    {
        var before = new Point(Left, Top);
        _drag = true;
        DragStarted?.Invoke(this);
        try { DragMove(); } catch (InvalidOperationException) { }   // the button was already up
        _drag = false; _down = null;
        Card.Unpress();
        if (Math.Abs(Left - before.X) + Math.Abs(Top - before.Y) > 2) Moved?.Invoke(this);
        else if (_edit) EditClicked?.Invoke(this);
        else Moved?.Invoke(this);   // put it back on the grid, end the drag look
    }

    public void ApplyTheme(WidgetTheme t, double strength = 0.5) => Card.Apply(t, Kind, strength);

    /// <summary>The user's choice for all widgets: "on top of everything" instead of down on the desktop.</summary>
    public static bool OnTop { get; set; }

    /// <summary>Nobody can see it (covered, full screen, screen off …): animations hold still until it is seen again.</summary>
    public bool Paused
    {
        get => _paused;
        set { if (value == _paused) return; _paused = value; PausedChanged?.Invoke(value); }
    }
    private bool _paused;
    public event Action<bool>? PausedChanged;

    /// <summary>Edit mode: in front, glowing, draggable, with the "−" button. Otherwise: down on the desktop.</summary>
    public bool EditMode
    {
        get => _edit;
        set
        {
            _edit = value;
            _remove.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _corner.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Card.Card.Cursor = value ? Cursors.SizeAll : Cursors.Hand;
            Card.Glow(value);
            Front = value || _raised || OnTop;
        }
    }

    /// <summary>Open: in front of everything and able to take the keyboard (text boxes in the expanded view).</summary>
    public bool Raised
    {
        get => _raised;
        set
        {
            _raised = value;
            if (_hwnd == IntPtr.Zero || !IsLoaded) return;
            int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, value ? ex & ~0x08000000 : ex | 0x08000000);
            Front = value || _edit || OnTop;
            if (value) { Activate(); Native.SetForegroundWindow(_hwnd); }
        }
    }

    private const int WM_WINDOWPOSCHANGING = 0x0046;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    /// <summary>Whatever asks to raise it, it stays on the desktop — except in edit mode or while open.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING && !_edit && !_raised && _owned && !Topmost)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            pos.hwndInsertAfter = new IntPtr(1);           // HWND_BOTTOM (owned by the desktop: just above the wallpaper)
            pos.flags &= ~0x0004u;                          // drop SWP_NOZORDER
            Marshal.StructureToPtr(pos, lParam, false);
        }
        return IntPtr.Zero;
    }

    private bool _owned;

    /// <summary>Explorer may start after us or restart: then the desktop window is new and the widget re-owned.</summary>
    public void EnsureOwned()
    {
        if (_hwnd == IntPtr.Zero || !IsLoaded) return;
        if (!Topmost && !LiveGlass.IsOwnedByDesktop(_hwnd)) { _owned = LiveGlass.OwnByDesktop(_hwnd); if (_owned && !_edit && !_raised) SendToBottom(); }
    }

    /// <summary>
    /// In front of every window (edit mode, open, dragged) or back down on the desktop. In front it stops being
    /// owned by the desktop (see <see cref="LiveGlass.Disown"/>); back down it is owned again.
    /// </summary>
    public bool Front
    {
        get => Topmost;
        set
        {
            if (value == Topmost && (value || _owned)) return;
            if (value)
            {
                if (_hwnd != IntPtr.Zero) { LiveGlass.Disown(_hwnd); _owned = false; }
                Topmost = true;
            }
            else
            {
                Topmost = false;
                if (_hwnd != IntPtr.Zero) _owned = LiveGlass.OwnByDesktop(_hwnd);
                SendToBottom();
            }
        }
    }

    public void SendToBottom()
    {
        if (_hwnd == IntPtr.Zero || !_owned || Topmost) return;
        Native.SetWindowPos(_hwnd, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }
}

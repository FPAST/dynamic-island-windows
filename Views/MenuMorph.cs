using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// Menus open like designbookmark's Dynamic menu: the panel springs out of a small pill — width and height on
/// their own springs, the height a little softer so it lands after it has widened — and the items rise in one by
/// one. Done with the menu window's region (what is shown of it, the glass blur included), so the menu's own
/// layout never changes; it grows from the corner at the pointer. Springs from menu.js.
/// </summary>
public static class MenuMorph
{
    private sealed class S(double x, double k, double c)
    {
        public double X = x, V, T = x;
        public readonly double K = k, C = Motion.Reduced ? 2 * Math.Sqrt(k) : c;
        public void Step(double dt) { double a = -K * (X - T) - C * V; V += a * dt; X += V * dt; }
        public bool Still(double eps) => Math.Abs(X - T) < eps && Math.Abs(V) < eps * 10;
    }

    // menu.js: width 320 / 26, height 240 / 22, the corner 380 / 24, each item 260 / 22 (after 80 ms, 50 ms apart)
    private const double PillW = 150, PillH = 46, Radius = 16;

    /// <summary>Tests: the shape last shown and the full size of the menu (DIPs), the first item's opacity.</summary>
    internal static (double W, double H, double R, double FullW, double FullH, double Item, bool Right, bool Bottom) Last;

    /// <summary>The menu has opened: grow it out of a pill. glass: blur behind it once it has a shape.</summary>
    public static void Open(ContextMenu menu, bool glass)
    {
        if (PresentationSource.FromVisual(menu) is not HwndSource src || src.RootVisual is not FrameworkElement root) return;
        root.UpdateLayout();
        double fw = root.ActualWidth, fh = root.ActualHeight;
        if (fw <= 0 || fh <= 0) return;
        if (Motion.Reduced || fw <= PillW + 1 && fh <= PillH + 1)
        {
            LiveGlass.Shape(src.Handle, root, 0, 0, fw, fh, Radius);
            if (glass) LiveGlass.Blur(src.Handle, true);
            return;
        }

        // grow from the corner nearest the pointer (a menu near the screen's edge opens up or to the left);
        // a menu opened by a button grows out of the button
        var at = menu.Placement != System.Windows.Controls.Primitives.PlacementMode.MousePoint && menu.PlacementTarget is FrameworkElement by && by.IsVisible
            ? by.TranslatePoint(new Point(by.ActualWidth / 2, by.ActualHeight / 2), root)
            : Mouse.GetPosition(root);
        bool fromRight = at.X > fw / 2, fromBottom = at.Y > fh / 2;
        var w = new S(Math.Min(PillW, fw), 320, 26);
        var h = new S(Math.Min(PillH, fh), 240, 22);
        var round = new S(0, 380, 24);
        w.T = fw; h.T = fh; round.T = 1;

        void Shape()
        {
            double ww = Math.Clamp(w.X, 1, fw + 40), hh = Math.Clamp(h.X, 1, fh + 40);
            double r = Math.Min(hh / 2, Radius + (1 - Math.Clamp(round.X, 0, 1)) * 10);
            LiveGlass.Shape(src.Handle, root, fromRight ? fw - ww : 0, fromBottom ? fh - hh : 0, ww, hh, r);
            Last = (ww, hh, r, fw, fh, Last.Item, fromRight, fromBottom);
        }
        Shape();
        if (glass) LiveGlass.Blur(src.Handle, true);

        // the items: each rises 18 px and fades in, one after another
        var items = new List<(FrameworkElement El, TranslateTransform Move, S Spring, double Wait)>();
        for (int i = 0; i < menu.Items.Count; i++)
        {
            if (menu.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement el || el.Visibility != Visibility.Visible) continue;
            var move = new TranslateTransform(0, 18);
            el.RenderTransform = move;
            el.Opacity = 0;
            items.Add((el, move, new S(0, 260, 22), .08 + items.Count * .05));
        }

        var last = TimeSpan.Zero;
        double clock = 0;
        EventHandler? frame = null;
        void End()
        {
            CompositionTarget.Rendering -= frame;
            foreach (var it in items) { it.El.Opacity = 1; it.El.RenderTransform = null; }
        }
        RoutedEventHandler? closed = null;
        closed = (_, _) => { menu.Closed -= closed; End(); };
        menu.Closed += closed;
        frame = (_, e) =>
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == last) return;
            double dt = last == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - last).TotalSeconds, 1 / 30.0);
            last = now;
            clock += dt;
            for (int s = 0; s < 4; s++)
            {
                w.Step(dt / 4); h.Step(dt / 4); round.Step(dt / 4);
                foreach (var it in items) if (clock >= it.Wait) { it.Spring.T = 1; it.Spring.Step(dt / 4); }
            }
            Shape();
            foreach (var it in items)
            {
                it.El.Opacity = Math.Clamp(it.Spring.X, 0, 1);
                it.Move.Y = (1 - it.Spring.X) * 18;
            }
            if (items.Count > 0) Last.Item = items[0].El.Opacity;
            if (w.Still(0.3) && h.Still(0.3) && round.Still(0.002) && items.All(it => it.Spring.T == 1 && it.Spring.Still(0.002)))
            {
                menu.Closed -= closed;
                LiveGlass.Shape(src.Handle, root, 0, 0, fw, fh, Radius);
                Last = (fw, fh, Radius, fw, fh, 1, fromRight, fromBottom);
                End();
            }
        };
        CompositionTarget.Rendering += frame;
    }
}

/// <summary>Two lines that meet in the middle and turn into a cross while open (menu.js: 380 / 24).</summary>
public sealed class MenuToggleIcon : FrameworkElement
{
    private double _x, _v, _t;
    private bool _ticking;
    private TimeSpan _last;

    public MenuToggleIcon() { Width = Height = 16; IsHitTestVisible = false; }

    public bool Open
    {
        get => _t > .5;
        set
        {
            _t = value ? 1 : 0;
            if (Motion.Reduced) { _x = _t; _v = 0; InvalidateVisual(); return; }
            if (_ticking) return;
            _ticking = true; _last = TimeSpan.Zero;
            CompositionTarget.Rendering += Frame;
        }
    }

    private void Frame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _last) return;
        double dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _last).TotalSeconds, 1 / 30.0);
        _last = now;
        for (int i = 0; i < 4; i++) { double a = -380 * (_x - _t) - 24 * _v; _v += a * dt / 4; _x += _v * dt / 4; }
        InvalidateVisual();
        if (Math.Abs(_x - _t) < 0.002 && Math.Abs(_v) < 0.02) { _x = _t; _ticking = false; CompositionTarget.Rendering -= Frame; }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, x = Math.Clamp(_x, -.1, 1.1);
        var pen = new Pen(TryFindResource("Fg") as Brush ?? Brushes.White, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var c = new Point(w / 2, h / 2);
        // two lines 7 px apart meet in the middle and turn 45° each way
        foreach (var (shift, turn) in new[] { (-3.5, 45.0), (3.5, -45.0) })
        {
            dc.PushTransform(new TranslateTransform(0, shift * (1 - x)));
            dc.PushTransform(new RotateTransform(turn * x, c.X, c.Y));
            dc.DrawLine(pen, new Point(2, c.Y), new Point(w - 2, c.Y));
            dc.Pop(); dc.Pop();
        }
    }
}

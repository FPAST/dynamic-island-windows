using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// "Live glass" (Settings > Appearance, off by default): instead of grabbing the screen, an ordinary window lies
/// right under the island in z-order, blurred by the Windows compositor itself every frame and cut to the island's
/// outline. Smooth whatever moves behind it; no lens at the rim (the light along the edge stays).
/// </summary>
public partial class MainWindow
{
    [DllImport("gdi32.dll")] private static extern IntPtr CreatePolygonRgn(Native.POINT[] pts, int count, int mode);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] private static extern IntPtr BeginDeferWindowPos(int n);
    [DllImport("user32.dll")] private static extern IntPtr DeferWindowPos(IntPtr info, IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool EndDeferWindowPos(IntPtr info);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);

    private const uint SwpNoSize = 0x1, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpShow = 0x40, SwpNoOwnerZOrder = 0x200;

    private Window? _liveBack;
    private IntPtr _liveBackHwnd;
    private bool _liveShown;
    /// <summary>The backdrop's place from the island window's corner, and its region (both in pixels).</summary>
    private Int32Rect _liveBox;
    private Native.POINT[] _liveRgn = [];

    private bool LiveGlassOn => _settings.LiveGlass && IslandGlass && !Lite;

    private void ShowLiveBackdrop(bool show)
    {
        if (show && _liveBack == null) CreateLiveBackdrop();
        if (_liveBackHwnd == IntPtr.Zero || show == _liveShown) return;
        _liveShown = show;
        if (show) { _liveRgn = []; LiveOutline(IslandShape.Data); }
        else ShowWindow(_liveBackHwnd, 0);   // SW_HIDE
    }

    private void CreateLiveBackdrop()
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            Background = Brushes.Transparent, Width = 1, Height = 1, Left = -32000, Top = -32000, Title = "Atoll glass backdrop",
        };
        w.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(w).Handle;
            // not in Alt+Tab or the taskbar, never activated, clicks go through to whatever is under it
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, Native.GetWindowLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | 0x08000000);   // WS_EX_NOACTIVATE
            HwndSource.FromHwnd(h)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
            {
                if (msg != 0x84) return IntPtr.Zero;   // WM_NCHITTEST
                handled = true;
                return new IntPtr(-1);                 // HTTRANSPARENT
            });
            LiveGlass.Prepare(w);
            // the blur-behind accent fills the whole window rectangle whatever its region (Windows 11 measured: square
            // corners); the compositor's host backdrop drawn into the window keeps to the region
            if (!HostBackdrop(h)) LiveGlass.Blur(h, true);
            Native.SetWindowDisplayAffinity(h, _excluded == true ? Native.WDA_EXCLUDEFROMCAPTURE : 0);
        };
        w.Show();   // off screen until the outline places it
        _liveBack = w;
        _liveBackHwnd = new WindowInteropHelper(w).Handle;
        ShowWindow(_liveBackHwnd, 0);
    }

    /// <summary>
    /// Every outline change (each frame of an animation): the region follows the island's shape, a pixel inside its
    /// rim so the region's stepped edge stays hidden under the island's smooth one. One SetWindowRgn at most, only
    /// when the shape changed.
    /// </summary>
    private void LiveOutline(Geometry? g)
    {
        if (!_liveShown || g == null || PresentationSource.FromVisual(Island) == null) return;
        var flat = g.GetFlattenedPathGeometry(0.75, ToleranceType.Absolute);
        if (flat.Figures.Count == 0) return;
        var toWindow = Island.TransformToAncestor(this);
        double dpi = Dpi;
        var pts = new List<Point>();
        foreach (var fig in flat.Figures)
        {
            pts.Add(fig.StartPoint);
            foreach (var seg in fig.Segments)
                if (seg is System.Windows.Media.PolyLineSegment pl) pts.AddRange(pl.Points);
                else if (seg is System.Windows.Media.LineSegment l) pts.Add(l.Point);
            break;   // the island is one shape
        }
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = toWindow.Transform(pts[i]);
            p = new Point(p.X * dpi, p.Y * dpi);
            pts[i] = p;
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y); maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        var c = new Point((minX + maxX) / 2, (minY + maxY) / 2);
        var box = new Int32Rect((int)Math.Floor(minX), (int)Math.Floor(minY), Math.Max(1, (int)Math.Ceiling(maxX - minX) + 1), Math.Max(1, (int)Math.Ceiling(maxY - minY) + 1));
        var rgn = new Native.POINT[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            var v = pts[i] - c;
            double len = v.Length;
            var p = len > 1.5 ? pts[i] - v / len * 1.2 : pts[i];   // a hair inside the rim
            rgn[i] = new Native.POINT { X = (int)Math.Round(p.X) - box.X, Y = (int)Math.Round(p.Y) - box.Y };
        }
        bool changed = rgn.Length != _liveRgn.Length;
        for (int i = 0; !changed && i < rgn.Length; i++) changed = rgn[i].X != _liveRgn[i].X || rgn[i].Y != _liveRgn[i].Y;
        bool moved = !box.Equals(_liveBox);
        _liveBox = box;
        if (moved || changed) PlaceLiveBackdrop();
        if (!changed) return;
        _liveRgn = rgn;
        SetWindowRgn(_liveBackHwnd, CreatePolygonRgn(rgn, rgn.Length, 2), true);   // WINDING; the system owns the region from here
    }

    [DllImport("user32.dll")] private static extern int GetWindowRgnBox(IntPtr h, out Native.RECT r);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr h, IntPtr rgn);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr rgn, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);

    /// <summary>Tests: the backdrop's window and region on the screen against the island's own rectangle (pixels).</summary>
    private string LiveBackdropCheck()
    {
        var tl = Island.PointToScreen(new Point(0, 0));
        var br = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
        Native.GetWindowRect(_liveBackHwnd, out var b);
        int kind = GetWindowRgnBox(_liveBackHwnd, out var r);   // 0 error/none, 1 null, 2 simple, 3 complex
        var probe = CreateRectRgn(0, 0, 1, 1);
        GetWindowRgn(_liveBackHwnd, probe);
        bool corner = PtInRegion(probe, 1, b.Bottom - b.Top - 2), middle = PtInRegion(probe, (b.Right - b.Left) / 2, (b.Bottom - b.Top) / 2);
        DeleteObject(probe);
        return $"island {tl.X:0},{tl.Y:0}-{br.X:0},{br.Y:0}; backdrop visible={IsWindowVisible(_liveBackHwnd)} window {b.Left},{b.Top}-{b.Right},{b.Bottom}; " +
               $"region kind {kind} at {b.Left + r.Left},{b.Top + r.Top}-{b.Left + r.Right},{b.Top + r.Bottom}, {_liveRgn.Length} points, bottom-left corner in it={corner}, middle in it={middle}";
    }

    // ---------------------------------------------------------------- the compositor's blurred backdrop

    [StructLayout(LayoutKind.Sequential)] private struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop { void CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out IntPtr target); }

    private IntPtr _liveQueue;
    private Windows.UI.Composition.Compositor? _liveCompositor;
    private Windows.UI.Composition.Desktop.DesktopWindowTarget? _liveTarget;

    /// <summary>A visual filling the window with what's behind it, blurred by the compositor. False where Windows can't.</summary>
    private bool HostBackdrop(IntPtr h)
    {
        try
        {
            if (_liveQueue == IntPtr.Zero && CreateDispatcherQueueController(new DispatcherQueueOptions { Size = 12, ThreadType = 2, ApartmentType = 2 }, out _liveQueue) != 0) return false;
            int on = 1;
            if (DwmSetWindowAttribute(h, 17, ref on, 4) != 0) return false;   // DWMWA_USE_HOSTBACKDROPBRUSH (Windows 11)
            var c = _liveCompositor ??= new Windows.UI.Composition.Compositor();
            WinRT.CastExtensions.As<ICompositorDesktopInterop>(c).CreateDesktopWindowTarget(h, true, out var raw);
            _liveTarget = Windows.UI.Composition.Desktop.DesktopWindowTarget.FromAbi(raw);
            var v = c.CreateSpriteVisual();
            v.RelativeSizeAdjustment = System.Numerics.Vector2.One;
            v.Brush = c.CreateHostBackdropBrush();
            _liveTarget.Root = v;
            Log.Write("live glass: the compositor's host backdrop");
            return true;
        }
        catch (Exception e) { Log.Error("live glass: host backdrop", e); return false; }
    }

    /// <summary>The backdrop right under the island in z-order, where the island window is now.</summary>
    private void PlaceLiveBackdrop()
    {
        if (!_liveShown || !Native.GetWindowRect(_hwnd, out var wr)) return;
        Native.SetWindowPos(_liveBackHwnd, _hwnd, wr.Left + _liveBox.X, wr.Top + _liveBox.Y, _liveBox.Width, _liveBox.Height, SwpNoActivate | SwpShow | SwpNoOwnerZOrder);
    }

    /// <summary>A step of a drag or glide: both windows move in one go, so they never part. False: no backdrop.</summary>
    private bool MoveWithLiveBackdrop(int x, int y)
    {
        if (!_liveShown) return false;
        var d = BeginDeferWindowPos(2);
        if (d == IntPtr.Zero) return false;
        d = DeferWindowPos(d, _hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
        if (d != IntPtr.Zero) d = DeferWindowPos(d, _liveBackHwnd, _hwnd, x + _liveBox.X, y + _liveBox.Y, _liveBox.Width, _liveBox.Height, SwpNoActivate | SwpNoOwnerZOrder);
        return d != IntPtr.Zero && EndDeferWindowPos(d);
    }
}

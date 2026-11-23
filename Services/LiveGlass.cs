using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// What the person asked Windows for, as Apple asks apps to respect: "Transparency effects" off means solid
/// surfaces instead of glass; "Animation effects" off means things move straight to where they go.
/// </summary>
public static class Motion
{
    public static bool Reduced => !SystemParameters.ClientAreaAnimation;

    private static bool _noTransparency;
    private static DateTime _checked;

    /// <summary>Windows' "Transparency effects" off (read at most every few seconds).</summary>
    public static bool NoTransparency
    {
        get
        {
            if ((DateTime.Now - _checked).TotalSeconds < 5) return _noTransparency;
            _checked = DateTime.Now;
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                _noTransparency = k?.GetValue("EnableTransparency") is int v && v == 0;
            }
            catch (Exception e) { Log.Write("transparency setting: " + e.GetType().Name); _noTransparency = false; }
            return _noTransparency;
        }
    }
}

/// <summary>
/// Glass that is drawn by the Windows compositor itself, live, every frame: whatever moves behind the window
/// (an animated wallpaper, a video, a window being dragged) is blurred as it moves — no screenshots. The window
/// must be a normal (not layered) WPF window with a transparent background; the shape of the glass is the
/// window's region (a rounded rectangle).
/// </summary>
public static class LiveGlass
{
    [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int State, Flags; public uint Gradient; public int Animation; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowCompositionData { public int Attribute; public IntPtr Data; public int Size; }
    [StructLayout(LayoutKind.Sequential)] private struct MARGINS { public int L, R, T, B; }

    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr h, ref WindowCompositionData d);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);

    /// <summary>
    /// Once, in SourceInitialized: see-through client area, no system border. roundCorners: Windows rounds the
    /// window itself (its blur ignores a window region, so this is the only way the blur gets round corners).
    /// </summary>
    public static void Prepare(Window w, bool roundCorners = false)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (HwndSource.FromHwnd(h) is { CompositionTarget: { } ct }) ct.BackgroundColor = Colors.Transparent;
        var m = new MARGINS { L = -1, R = -1, T = -1, B = -1 };
        try { DwmExtendFrameIntoClientArea(h, ref m); } catch { /* no DWM composition: a plain window */ }
        int noRound = roundCorners ? 2 : 1, noBorder = unchecked((int)0xFFFFFFFE);   // DWMWCP_ROUND / DONOTROUND
        try { DwmSetWindowAttribute(h, 33, ref noRound, 4); DwmSetWindowAttribute(h, 34, ref noBorder, 4); } catch { /* older Windows: no such attribute */ }
    }

    private static bool _logged;

    /// <summary>Turns the live blur behind the window on or off.</summary>
    public static void Blur(IntPtr h, bool on)
    {
        var accent = new AccentPolicy { State = on ? 3 : 0 };   // ACCENT_ENABLE_BLURBEHIND: clear, fast, no lag while moving
        int size = Marshal.SizeOf(accent);
        var p = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, p, false);
            var d = new WindowCompositionData { Attribute = 19, Data = p, Size = size };   // WCA_ACCENT_POLICY
            int ok = SetWindowCompositionAttribute(h, ref d);
            if (!_logged || ok == 0) { _logged = true; Log.Write($"live glass: blur {(on ? "on" : "off")}, accepted={ok != 0}"); }
        }
        catch (Exception e) { Log.Write("live glass: " + e.Message); }
        finally { Marshal.FreeHGlobal(p); }
    }

    /// <summary>The shape of the glass: a rounded rectangle the size of the window (DIPs → pixels).</summary>
    public static void Round(Window w, double radius, double width = double.NaN, double height = double.NaN)
    {
        var h = new WindowInteropHelper(w).Handle;
        double ww = !double.IsNaN(width) ? width : w.ActualWidth > 0 ? w.ActualWidth : w.Width;
        double wh = !double.IsNaN(height) ? height : w.ActualHeight > 0 ? w.ActualHeight : w.Height;
        if (h == IntPtr.Zero || !(ww > 0) || !(wh > 0)) return;
        var dpi = VisualTreeHelper.GetDpi(w);
        // a hair rounder than the card itself: the region's edge is pixel-stepped, the card's is smooth, so the
        // card must cover the region everywhere (otherwise bare glass peeks out at the corners)
        int pw = (int)Math.Round(ww * dpi.DpiScaleX), ph = (int)Math.Round(wh * dpi.DpiScaleY), r = (int)Math.Round((radius + 1.5) * 2 * dpi.DpiScaleX);
        SetWindowRgn(h, CreateRoundRectRgn(0, 0, pw + 1, ph + 1, r, r), true);   // the system owns the region from here
    }

    /// <summary>
    /// Shows only this rounded part of a popup window (x, y, width, height in its own DIPs): a menu growing out of a
    /// pill (Views.MenuMorph) is this region, set frame by frame — the glass blur behind follows it.
    /// </summary>
    public static void Shape(IntPtr h, Visual root, double x, double y, double width, double height, double radius)
    {
        if (h == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(root);
        int px = (int)Math.Round(x * dpi.DpiScaleX), py = (int)Math.Round(y * dpi.DpiScaleY);
        int pw = (int)Math.Round(width * dpi.DpiScaleX), ph = (int)Math.Round(height * dpi.DpiScaleY), r = (int)Math.Round((radius + 1.5) * 2 * dpi.DpiScaleX);
        SetWindowRgn(h, CreateRoundRectRgn(px, py, px + pw + 1, py + ph + 1, r, r), true);
    }

    /// <summary>A menu (or any popup) of live glass: blurred behind, cut to a rounded rectangle.</summary>
    public static void Popup(Visual root, double radius)
    {
        if (PresentationSource.FromVisual(root) is not HwndSource src || src.RootVisual is not FrameworkElement fe) return;
        // shape first, then blur: blur on a window without its shape shows square corners for a moment
        var dpi = VisualTreeHelper.GetDpi(fe);
        int pw = (int)Math.Round(fe.ActualWidth * dpi.DpiScaleX), ph = (int)Math.Round(fe.ActualHeight * dpi.DpiScaleY), r = (int)Math.Round((radius + 1.5) * 2 * dpi.DpiScaleX);
        if (pw <= 0 || ph <= 0) return;
        SetWindowRgn(src.Handle, CreateRoundRectRgn(0, 0, pw + 1, ph + 1, r, r), true);
        Blur(src.Handle, true);
    }

    /// <summary>
    /// Lives on the desktop: owned by the desktop window, so it is always just above the wallpaper — never
    /// under it (which is what "the bottom of the z-order" alone can do) and not hidden by "Show desktop".
    /// </summary>
    public static bool OwnByDesktop(IntPtr h)
    {
        var desktop = Native.FindWindow("Progman", null);
        if (desktop != IntPtr.Zero) { SetWindowLongPtr(h, -8, desktop); return true; }   // GWLP_HWNDPARENT
        if (!_noDesktopLogged) { _noDesktopLogged = true; Log.Write("widgets: no desktop window (Progman) yet, will try again"); }
        return false;
    }

    private static bool _noDesktopLogged;

    /// <summary>
    /// Not owned by the desktop any more (while in front of everything): windows owned by the same window are moved
    /// as a group, so raising one owned widget would push the others that were raised before back under.
    /// </summary>
    public static void Disown(IntPtr h) => SetWindowLongPtr(h, -8, IntPtr.Zero);

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);

    public static bool IsOwnedByDesktop(IntPtr h)
    {
        var desktop = Native.FindWindow("Progman", null);
        return desktop != IntPtr.Zero && GetWindow(h, 4) == desktop;   // GW_OWNER
    }
}

/// <summary>A window gliding to a new place and size on a spring (a little overshoot, then settles).</summary>
public sealed class WindowSpring
{
    private readonly Window _w;
    private readonly double _pad;
    private double[] _x = new double[4], _v = new double[4], _to = new double[4];
    private bool _running;
    private Action? _done;
    private TimeSpan _last;
    public double Stiffness = 260, Damping = 26;

    /// <summary>pad: the window is this much larger than what it shows on every side; targets are given without it.</summary>
    public WindowSpring(Window w, double pad = 0) { _w = w; _pad = pad; }

    /// <summary>Every frame, before the window takes the new place and size (window rectangle, with the pad).</summary>
    public event Action<Rect>? Resizing;

    public bool Running => _running;

    /// <summary>Stops where it is (the window closed): nothing more is done to it.</summary>
    public void Stop() { _running = false; _done = null; CompositionTargetHook(false); }

    /// <summary>Called every frame with the current bounds (for things that follow the window).</summary>
    public event Action<Rect>? Step;

    public void To(Rect target, Action? done = null)
    {
        if (!_running)
        {
            _x = [_w.Left + _pad, _w.Top + _pad, _w.Width - 2 * _pad, _w.Height - 2 * _pad];
            _v = new double[4];
            _running = true;
            _last = TimeSpan.Zero;
            CompositionTargetHook(true);
        }
        _to = [target.X, target.Y, target.Width, target.Height];
        _done = done;
        if (Motion.Reduced)
        {
            // no animations wanted: straight there
            _x = (double[])_to.Clone(); _v = new double[4];
            Apply();
            Step?.Invoke(target);
            _running = false; CompositionTargetHook(false);
            var d = _done; _done = null; d?.Invoke();
        }
    }

    private void Apply()
    {
        var r = new Rect(_x[0] - _pad, _x[1] - _pad, Math.Max(20, _x[2]) + 2 * _pad, Math.Max(20, _x[3]) + 2 * _pad);
        Resizing?.Invoke(r);
        _w.Left = r.X; _w.Top = r.Y; _w.Width = r.Width; _w.Height = r.Height;
    }

    private void CompositionTargetHook(bool on)
    {
        if (on) CompositionTarget.Rendering += Tick; else CompositionTarget.Rendering -= Tick;
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _last).TotalSeconds, 0.001, 1 / 30.0);
        if (now == _last) return;
        _last = now;
        bool settled = true;
        for (int i = 0; i < 4; i++)
        {
            double a = (_to[i] - _x[i]) * Stiffness - _v[i] * Damping;
            _v[i] += a * dt;
            _x[i] += _v[i] * dt;
            if (Math.Abs(_to[i] - _x[i]) > 0.4 || Math.Abs(_v[i]) > 4) settled = false;
        }
        if (settled) _x = (double[])_to.Clone();
        Apply();
        Step?.Invoke(new Rect(_x[0], _x[1], _x[2], _x[3]));
        if (settled)
        {
            _running = false;
            CompositionTargetHook(false);
            var d = _done; _done = null;
            d?.Invoke();
        }
    }
}

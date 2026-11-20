using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland.Services;

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct PxRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>
/// When the widgets stop drawing and polling: a full-screen game or video, the screen off or locked, the user's
/// "pause" in the tray — all of them at once; a widget hidden behind windows — that one alone. A paused widget keeps
/// its last picture; it only stops animating, so it is back at once when uncovered.
/// </summary>
public static class WidgetPause
{
    public static bool All(bool fullscreen, bool displayOff, bool locked, bool userPaused) => fullscreen || displayOff || locked || userPaused;

    /// <summary>
    /// Is the widget hidden by these windows (front to back, already without ours and the desktop)? Checked on a
    /// 5×5 grid of points: every one must be under some window. Windows that overlap only partly don't count.
    /// </summary>
    public static bool Covered(PxRect widget, IReadOnlyList<PxRect> windows)
    {
        if (widget.Width <= 0 || widget.Height <= 0 || windows.Count == 0) return false;
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
            {
                double x = widget.Left + (i + 0.5) * widget.Width / 5, y = widget.Top + (j + 0.5) * widget.Height / 5;
                bool under = false;
                foreach (var w in windows) if (w.Contains(x, y)) { under = true; break; }
                if (!under) return false;
            }
        return true;
    }

    // ------------------------------------------------------------------ the windows on screen

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out PxRect value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out PxRect r);

    /// <summary>The visible windows of other programs, as drawn (not minimised, not hidden on another desktop).</summary>
    public static List<PxRect> OtherWindows()
    {
        var list = new List<PxRect>();
        uint ours = (uint)Environment.ProcessId;
        var cls = new StringBuilder(64);
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == ours) return true;
            if (DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;   // DWMWA_CLOAKED
            cls.Clear(); GetClassName(h, cls, cls.Capacity);
            if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if ((GetWindowLong(h, -20) & 0x20) != 0) return true;   // WS_EX_TRANSPARENT: overlays the mouse goes through
            if (DwmGetWindowAttribute(h, 9, out PxRect r, Marshal.SizeOf<PxRect>()) != 0 && !GetWindowRect(h, out r)) return true;   // DWMWA_EXTENDED_FRAME_BOUNDS
            if (r.Width > 40 && r.Height > 40) list.Add(r);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static PxRect RectOf(IntPtr hwnd) => GetWindowRect(hwnd, out var r) ? r : default;

    // ------------------------------------------------------------------ the screen on or off

    /// <summary>GUID_CONSOLE_DISPLAY_STATE: the screen turned off, dimmed or on.</summary>
    public static readonly Guid DisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
    public const int WM_POWERBROADCAST = 0x0218, PBT_POWERSETTINGCHANGE = 0x8013;

    [DllImport("user32.dll")] private static extern IntPtr RegisterPowerSettingNotification(IntPtr h, ref Guid g, int flags);

    public static void WatchDisplay(IntPtr hwnd)
    {
        var g = DisplayState;
        RegisterPowerSettingNotification(hwnd, ref g, 0);
    }

    /// <summary>From a WM_POWERBROADCAST / PBT_POWERSETTINGCHANGE message: is the screen off? (null: another setting)</summary>
    public static bool? DisplayOff(IntPtr lParam)
    {
        var setting = Marshal.PtrToStructure<Guid>(lParam);
        if (setting != DisplayState) return null;
        int data = Marshal.ReadInt32(lParam, 20);   // GUID (16) + DataLength (4), then the value: 0 off, 1 on, 2 dimmed
        return data == 0;
    }
}

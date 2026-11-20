using System.Windows;
using System.Windows.Interop;
using DynamicIsland.Services;
using DynamicIsland.Views;

namespace DynamicIsland;

/// <summary>
/// Widgets rest when nobody can see them: a full-screen game or video, the screen off, the session locked, or the
/// "pause" in the tray stops them all (drawing, animations, polling); a widget hidden behind windows stops alone.
/// Checked once a second in the island's slow tick (one EnumWindows, well under a millisecond).
/// </summary>
public partial class MainWindow
{
    private bool _displayOff, _sessionLocked, _widgetsAllPaused, _pauseWatching;

    /// <summary>Why the widgets are paused now ("" = they aren't), for the log and the tray.</summary>
    private string _pauseReason = "";

    private void WatchWidgetPause()
    {
        if (_pauseWatching || _hwnd == IntPtr.Zero) return;
        _pauseWatching = true;
        WidgetPause.WatchDisplay(_hwnd);
        Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
        {
            _sessionLocked = e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect
                                      or Microsoft.Win32.SessionSwitchReason.RemoteDisconnect;
            Dispatcher.BeginInvoke(UpdateWidgetPause);
        };
    }

    /// <summary>The island's window procedure passes the screen-state messages here.</summary>
    private void OnPowerSetting(IntPtr lParam)
    {
        if (WidgetPause.DisplayOff(lParam) is not { } off || off == _displayOff) return;
        _displayOff = off;
        Dispatcher.BeginInvoke(UpdateWidgetPause);
    }

    private void UpdateWidgetPause()
    {
        if (_widgets.Count == 0) return;
        bool fullscreen = !Settings.ReadOnly && _hwnd != IntPtr.Zero && Native.IsFullscreenAppActive(_hwnd);
        bool all = WidgetPause.All(fullscreen, _displayOff, _sessionLocked, _settings.WidgetsPaused) && !_widgetsEditing;
        string reason = !all ? "" : _settings.WidgetsPaused ? "paused by the user" : fullscreen ? "full screen" : _displayOff ? "screen off" : "locked";
        if (reason != _pauseReason) { Log.Write("widgets: " + (reason.Length > 0 ? "paused (" + reason + ")" : "running")); _pauseReason = reason; }

        // one at a time: hidden behind other programs' windows (only widgets down on the desktop can be)
        List<PxRect>? windows = null;
        foreach (var (key, w) in _widgets.ToList())
        {
            bool covered = false;
            if (!all && !w.Front && !Settings.ReadOnly)
            {
                windows ??= WidgetPause.OtherWindows();
                var r = WidgetPause.RectOf(new WindowInteropHelper(w).Handle);
                int inset = (int)(w.Pad * (PresentationSource.FromVisual(w)?.CompositionTarget?.TransformToDevice.M11 ?? 1));
                covered = WidgetPause.Covered(new PxRect(r.Left + inset, r.Top + inset, r.Right - inset, r.Bottom - inset), windows);
            }
            bool was = w.Paused;
            w.Paused = all || covered;
            // seen again: brought up to date at once, not on the next tick
            if (was && !w.Paused && !all && _widgetUpdaters.TryGetValue(key, out var update)) try { update(); } catch (Exception e) { Log.Write($"widget {key}: {e.Message}"); }
        }
        if (all != _widgetsAllPaused)
        {
            _widgetsAllPaused = all;
            if (all) { _widgetTimer.Stop(); _vizTimer.Stop(); _viz.Stop(); _lyricsTimer.Stop(); }
            else { _widgetTimer.Start(); TickWidgets(); }
        }
    }

    /// <summary>The tray's "pause widgets".</summary>
    private void ToggleWidgetsPaused()
    {
        _settings.WidgetsPaused = !_settings.WidgetsPaused;
        _settings.Save();
        UpdateWidgetPause();
    }

    /// <summary>"On the desktop" (under windows) or "on top of everything".</summary>
    private void SetWidgetsOnTop(bool on)
    {
        _settings.WidgetsOnTop = DesktopWidgetWindow.OnTop = on;
        _settings.Save();
        foreach (var w in _widgets.Values) w.Front = on || w.EditMode || w.Raised;
    }

    /// <summary>A widget's window is paused (covered, or all are): true when its animations should hold still.</summary>
    private static bool PausedHost(DependencyObject e) => Window.GetWindow(e) is DesktopWidgetWindow w && w.Paused;
}

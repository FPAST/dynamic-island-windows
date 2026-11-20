using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Views;

namespace DynamicIsland;

/// <summary>
/// The music widget's visualiser. The system's sound is captured only while a music widget with a visualiser is
/// on screen, something plays and it isn't silent; after 2 s of silence, on pause, in a full-screen game or when
/// no such widget is left, capture and drawing stop completely (nothing ticks at 30 fps for nothing).
/// </summary>
public partial class MainWindow
{
    private readonly AudioVisualizer _viz = new();
    private readonly List<WeakReference<VisualizerView>> _vizViews = [];
    private readonly DispatcherTimer _vizTimer = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
    private Color _vizColor = Colors.White;
    /// <summary>Tests: made-up bands instead of the sound (there may be nothing playing).</summary>
    private Func<float[]>? _vizDemo;

    private VizStyle? MusicVizStyle => _settings.MusicViz switch { "bars" => VizStyle.Bars, "ring" => VizStyle.Ring, "off" => null, _ => VizStyle.Wave };

    private void RegisterViz(VisualizerView v)
    {
        if (_vizViews.Count == 0) _vizTimer.Tick += (_, _) => Measure("widget visualiser", VizFrame);
        _vizViews.Add(new WeakReference<VisualizerView>(v));
        v.Color = _vizColor;
    }

    private IEnumerable<VisualizerView> LiveVizViews()
    {
        _vizViews.RemoveAll(r => !r.TryGetTarget(out var v) || PresentationSource.FromVisual(v) == null && v.IsLoaded);
        foreach (var r in _vizViews) if (r.TryGetTarget(out var v) && v.IsVisible && !PausedHost(v)) yield return v;
    }

    private void VizFrame()
    {
        float[] bands;
        if (_vizDemo != null) bands = _vizDemo();
        else { _viz.Frame(); bands = _viz.Bands; }
        foreach (var v in LiveVizViews()) { v.Color = _vizColor; v.Push(bands); }
    }

    /// <summary>Once a second: start or stop the capture and the 30 fps drawing.</summary>
    private void VizControl()
    {
        bool views = LiveVizViews().Any();
        if (_vizDemo != null) { if (views && !_vizTimer.IsEnabled) _vizTimer.Start(); return; }
        bool want = views && MusicVizStyle != null && _lastMedia?.IsPlaying == true && !_fullscreenHidden && !_gameMode
                    && (DateTime.Now - _viz.FailedAt).TotalSeconds > 30;
        if (_viz.Running)
        {
            if (!want || _viz.SilentFor.TotalSeconds > 2)
            {
                _viz.Stop();
                _vizTimer.Stop();
                foreach (var v in LiveVizViews()) v.Clear();
            }
        }
        else if (want && _audio.Peak > 0.01f)
        {
            _viz.Start();
            if (_viz.Running) _vizTimer.Start();
        }
    }

    /// <summary>The visualiser of a music widget of this size, in the style chosen (null: off).</summary>
    private VisualizerView? MusicViz(WidgetSize size)
    {
        if (MusicVizStyle is not { } style || size == WidgetSize.Small) return null;
        var v = new VisualizerView { Kind = style };
        RegisterViz(v);
        return v;
    }

    /// <summary>The visualiser's colour: the cover's colour, lightened so it glows on the cover-tinted glass.</summary>
    private void SetVizColor(Color dominant)
    {
        _vizColor = Color.FromRgb((byte)(dominant.R + (255 - dominant.R) * 0.45), (byte)(dominant.G + (255 - dominant.G) * 0.45), (byte)(dominant.B + (255 - dominant.B) * 0.45));
    }
}

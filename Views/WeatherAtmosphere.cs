using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland.Views;

/// <summary>
/// A very slow, barely visible background for the weather card: drifting mist in fog, a few drops running down
/// the glass in rain, falling snow, a warm glare when it is clear. 8 frames a second at most, and only while it
/// is seen (the caller stops it when the widget is paused, Windows' animations are off or the user turned it off).
/// </summary>
public sealed class WeatherAtmosphere : FrameworkElement
{
    public enum Kind { None, Mist, Rain, Snow, Glare }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private readonly Random _rnd = new(7);
    private readonly List<(double X, double Y, double Speed, double Size, double Phase)> _bits = [];
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private Kind _kind;
    private bool _wanted;

    public WeatherAtmosphere()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => InvalidateVisual();
        IsVisibleChanged += (_, _) => Apply();
        Unloaded += (_, _) => _timer.Stop();
        Loaded += (_, _) => Apply();
    }

    /// <summary>The weather code (WMO) and whether it is day decide the kind.</summary>
    public static Kind For(int code, bool isDay) => code switch
    {
        45 or 48 => Kind.Mist,
        >= 51 and <= 67 or >= 80 and <= 82 or >= 95 => Kind.Rain,
        >= 71 and <= 77 or 85 or 86 => Kind.Snow,
        0 or 1 when isDay => Kind.Glare,
        _ => Kind.None,
    };

    public void Show(Kind kind)
    {
        if (kind != _kind)
        {
            _kind = kind;
            _bits.Clear();
            int n = kind switch { Kind.Rain => 9, Kind.Snow => 16, Kind.Mist => 4, _ => 0 };
            for (int i = 0; i < n; i++) _bits.Add((_rnd.NextDouble(), _rnd.NextDouble(), 0.4 + _rnd.NextDouble() * 0.6, 0.5 + _rnd.NextDouble(), _rnd.NextDouble() * 6.28));
            InvalidateVisual();
        }
        Apply();
    }

    /// <summary>Run or hold still (the picture stays as it is).</summary>
    public bool Running
    {
        get => _wanted;
        set { _wanted = value; Apply(); }
    }

    private void Apply()
    {
        bool run = _wanted && _kind != Kind.None && IsVisible && IsLoaded;
        if (run && !_timer.IsEnabled) _timer.Start();
        else if (!run && _timer.IsEnabled) _timer.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, t = _clock.Elapsed.TotalSeconds;
        if (w <= 0 || h <= 0 || _kind == Kind.None) return;
        switch (_kind)
        {
            case Kind.Glare:
            {
                // a warm light in the upper corner, breathing over half a minute
                double a = 0.16 + 0.05 * Math.Sin(t * 2 * Math.PI / 30);
                var c = Color.FromRgb(0xFF, 0xD0, 0x80);
                var b = new RadialGradientBrush(Color.FromArgb((byte)(255 * a), c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B))
                { Center = new Point(0.85, 0.05), GradientOrigin = new Point(0.85, 0.05), RadiusX = 0.7, RadiusY = 0.7 };
                dc.DrawRectangle(b, null, new Rect(0, 0, w, h));
                break;
            }
            case Kind.Mist:
                foreach (var (x, y, speed, size, phase) in _bits)
                {
                    // wide soft clouds drifting sideways, a card width in about two minutes
                    double cx = ((x + t * speed / 120) % 1.4 - 0.2) * w, cy = (0.25 + y * 0.6) * h;
                    var b = new RadialGradientBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF));
                    dc.DrawEllipse(b, null, new Point(cx, cy), w * 0.45 * size, h * 0.22 * size);
                }
                break;
            case Kind.Rain:
                foreach (var (x, y, speed, size, phase) in _bits)
                {
                    // drops on the glass: sliding down slowly, a faint trail behind
                    double p = (y + t * speed / 14) % 1.2 - 0.1;
                    double cx = x * w + Math.Sin(t * 0.3 + phase) * 2, cy = p * h, r = 1.6 + size * 1.4;
                    var trail = new LinearGradientBrush(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF), 90);
                    dc.DrawRoundedRectangle(trail, null, new Rect(cx - r * 0.4, cy - 26 * size, r * 0.8, 26 * size), r * 0.4, r * 0.4);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), null, new Point(cx, cy), r * 0.8, r);
                }
                break;
            case Kind.Snow:
                foreach (var (x, y, speed, size, phase) in _bits)
                {
                    double p = (y + t * speed / 40) % 1.1 - 0.05;
                    double cx = x * w + Math.Sin(t * 0.6 + phase) * 8, cy = p * h;
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(0x60 + 0x40 * size / 1.5), 0xFF, 0xFF, 0xFF)), null, new Point(cx, cy), 1.4 * size + 0.8, 1.4 * size + 0.8);
                }
                break;
        }
    }
}

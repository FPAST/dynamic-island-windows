using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// The song's words on a widget: the line being sung is bright and a little larger, the ones around it dimmed;
/// the text scrolls smoothly to keep that line in the upper third. A click on a line moves the song there.
/// Plain (untimed) text just scrolls along with the song. With no text, a short message instead of a blank.
/// </summary>
public sealed class LyricsView : Grid
{
    private const double Dimmed = 0.42, Larger = 1.07;

    private readonly StackPanel _panel = new();
    // the lines live on a canvas: inside a grid cell, the overflowing panel would be cut to the cell before it is moved
    private readonly Canvas _canvas = new();
    private readonly TranslateTransform _shift = new();
    private readonly TextBlock _message = new()
    {
        FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
        TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
    };
    private Lyrics? _shown;
    private int _index = int.MinValue;
    private double _fraction = -1;

    /// <summary>A line was clicked: the time it starts at.</summary>
    public event Action<TimeSpan>? LineClicked;

    /// <summary>False while widgets are being arranged: clicks then belong to dragging.</summary>
    public Func<bool> Clickable { get; set; } = () => true;

    public double LineSize { get; init; } = 14;

    public LyricsView()
    {
        ClipToBounds = true;
        _panel.RenderTransform = _shift;
        _canvas.Children.Add(_panel);
        Children.Add(_canvas);
        Children.Add(_message);
        // the edges fade out instead of cutting lines in half. In absolute units: relative ones would be measured
        // over all the (overflowing, clipped) lines, not over the visible block
        var fade = new LinearGradientBrush(
            [new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.16), new GradientStop(Colors.Black, 0.82), new GradientStop(Colors.Transparent, 1)],
            new Point(0, 0), new Point(0, 1)) { MappingMode = BrushMappingMode.Absolute };
        OpacityMask = fade;
        SizeChanged += (_, _) => { fade.EndPoint = new Point(0, ActualHeight); _panel.Width = ActualWidth; Scroll(false); };
    }

    /// <summary>Shows a message instead of the words ("Текст не найден" and the like).</summary>
    public void ShowMessage(string text)
    {
        if (_shown != null || _panel.Children.Count > 0) { _panel.Children.Clear(); _shown = null; }
        _message.Text = text;
        _message.Visibility = Visibility.Visible;
    }

    /// <summary>The words of this song (does nothing when they are already shown).</summary>
    public void Show(Lyrics lyrics)
    {
        if (ReferenceEquals(_shown, lyrics)) return;
        _shown = lyrics;
        _index = int.MinValue; _fraction = -1;
        _message.Visibility = Visibility.Collapsed;
        _panel.Children.Clear();
        _shift.BeginAnimation(TranslateTransform.YProperty, null);
        _shift.Y = 0;
        foreach (var line in lyrics.Lines)
        {
            var t = new TextBlock
            {
                Text = line.Text.Length > 0 ? line.Text : "♪",
                FontSize = LineSize, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 7),
                Opacity = lyrics.Synced ? Dimmed : 0.8,
                RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new ScaleTransform(1, 1),
                Background = Brushes.Transparent,
            };
            if (lyrics.Synced)
            {
                t.Cursor = Cursors.Hand;
                var at = line.Time;
                t.MouseLeftButtonUp += (_, e) => { if (!Clickable()) return; e.Handled = true; LineClicked?.Invoke(at); };
            }
            _panel.Children.Add(t);
        }
    }

    /// <summary>Moves to the song's position: the line being sung (synced text) or how far the song is (plain).</summary>
    public void Follow(TimeSpan at, TimeSpan duration)
    {
        if (_shown is not { } l || l.Lines.Count == 0) return;
        if (l.Synced)
        {
            int i = LyricsService.LineAt(l.Lines, at + TimeSpan.FromMilliseconds(200));
            if (i == _index) return;
            Light(_index, false);
            _index = i;
            Light(i, true);
            Scroll(true);
        }
        else
        {
            double f = duration > TimeSpan.Zero ? Math.Clamp(at / duration, 0, 1) : 0;
            if (Math.Abs(f - _fraction) < 0.002) return;
            _fraction = f;
            Scroll(false);
        }
    }

    private void Light(int i, bool on)
    {
        if (i < 0 || i >= _panel.Children.Count || _panel.Children[i] is not TextBlock t) return;
        var dur = TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 280);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        t.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : Dimmed, dur) { EasingFunction = ease });
        var s = (ScaleTransform)t.RenderTransform;
        s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(on ? Larger : 1, dur) { EasingFunction = ease });
        s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(on ? Larger : 1, dur) { EasingFunction = ease });
    }

    private void Scroll(bool animate)
    {
        if (_shown is not { } l || ActualHeight <= 0 || _panel.Children.Count == 0) return;
        _panel.UpdateLayout();
        double y;
        if (l.Synced)
        {
            // the current line sits in the upper third; before the first line, the text starts at the top
            if (_index < 0) y = ActualHeight * 0.3;
            else
            {
                var line = (FrameworkElement)_panel.Children[_index];
                y = ActualHeight * 0.3 - line.TranslatePoint(new Point(0, 0), _panel).Y;
            }
        }
        else y = -Math.Max(0, _panel.ActualHeight - ActualHeight * 0.7) * Math.Max(0, _fraction) + ActualHeight * 0.15;

        if (!animate || Motion.Reduced) { _shift.BeginAnimation(TranslateTransform.YProperty, null); _shift.Y = y; return; }
        _shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(y, TimeSpan.FromMilliseconds(520)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// The music widget: what plays in any app that reports to Windows (Spotify, browsers, AIMP…). Each size has its
/// own layout — S: the cover fills it, title and play on frosted glass at the bottom; M: cover, text, the thin
/// timeline, the controls; L: bigger, with the times and the app's volume. The glass takes the cover's colour
/// and changes with the track; the mouse wheel sets the playing app's own volume.
/// </summary>
public partial class MainWindow
{
    /// <summary>Demo pictures: two apps, so the switcher shows.</summary>
    private IReadOnlyList<(string AppId, string Name, bool Playing)> MediaSessions() =>
        Settings.ReadOnly ? [("Spotify.exe", "Spotify", true), ("chrome", "Chrome", false)] : _media.Sessions();

    private Action MusicWidget(Grid body, Border wash)
    {
        var size = _buildSize;
        var root = (Grid)body.Parent;
        LightText(body);
        // the cover's dark colour carries white text; a text shadow would be redrawn with the visualiser every frame
        if (Window.GetWindow(root) is Views.DesktopWidgetWindow host) { host.Card.NoTextShadow = true; host.ApplyTheme(host.Card.Theme, GlassOf("music")); }

        // the cover's colour, under everything: animated from one track's colour to the next
        var mood = new SolidColorBrush(Color.FromRgb(0x3A, 0x2A, 0x5A));
        var moodLayer = new Grid();
        moodLayer.Children.Add(new Border { Background = mood, Opacity = 0.78 });
        moodLayer.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(0x00, 0, 0, 0), Color.FromArgb(0x55, 0, 0, 0), 90) });
        wash.Child = moodLayer;

        var cover = new Grid { ClipToBounds = true };
        var title = W("", size == WidgetSize.Large ? 17 : size == WidgetSize.Medium ? 15 : 13, FontWeights.SemiBold);
        var artist = W("", size == WidgetSize.Small ? 11 : 12.5, null, Dim);
        var playIcon = Glyph("", size == WidgetSize.Small ? 13 : size == WidgetSize.Large ? 20 : 15);
        var play = WKey(playIcon, Solid(0xFF, 0xFF, 0xFF, 0x30), null, () => _media.TogglePlayPause(), size == WidgetSize.Small ? 34 : size == WidgetSize.Large ? 58 : 44,
                        size == WidgetSize.Small ? 34 : size == WidgetSize.Large ? 50 : 38, name: L.T("Пауза или воспроизведение"));
        (Grid Track, Border Fill)? timeline = null;
        TextBlock? posText = null, lenText = null;
        TextBlock? appName = null;
        Border? switcher = null;
        Views.MusicRecordView? record = null;
        Views.MusicBarsView? bars = null;

        Border Controls(double h)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var prev = WKey(Glyph("", h > 44 ? 17 : 13), Brushes.Transparent, null, () => _media.Previous(), h, h, name: L.T("Предыдущий трек"));
            var next = WKey(Glyph("", h > 44 ? 17 : 13), Brushes.Transparent, null, () => _media.Next(), h, h, name: L.T("Следующий трек"));
            play.Margin = new Thickness(h > 44 ? 8 : 4, 0, h > 44 ? 8 : 4, 0);
            row.Children.Add(prev); row.Children.Add(play); row.Children.Add(next);
            var g = GlassGroup(row, h / 2 + 4);
            g.Padding = new Thickness(4);
            return g;
        }

        Border Switcher()
        {
            appName = W("", 10.5, FontWeights.SemiBold, Dim);
            var arrows = Glyph("", 9, Dim); arrows.Margin = new Thickness(5, 1, 0, 0);
            var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { appName, arrows } };
            var b = new Border { Child = content, Background = Brushes.Transparent, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 0, 6, 2) };
            b.ToolTip = L.T("Следующий источник");
            b.MouseLeftButtonUp += (_, e) => { if (_widgetsEditing) return; e.Handled = true; _media.ChooseNext(); TickWidgets(); };
            arrows.Tag = "arrows";
            return b;
        }

        Grid Timeline(bool times)
        {
            var (track, fill) = Bar(3, Brushes.White);
            var knob = GlassKnob(track, fill, 3);
            track.Cursor = Cursors.Hand; track.Background = Brushes.Transparent;
            track.MouseLeftButtonDown += (_, e) => { if (_widgetsEditing) return; e.Handled = true; track.CaptureMouse(); knob(true); fill.Width = Math.Clamp(e.GetPosition(track).X, 0, track.ActualWidth); };
            track.MouseMove += (_, e) => { if (track.IsMouseCaptured) fill.Width = Math.Clamp(e.GetPosition(track).X, 0, track.ActualWidth); };
            track.MouseLeftButtonUp += (_, e) =>
            {
                if (!track.IsMouseCaptured) return;
                e.Handled = true; track.ReleaseMouseCapture(); knob(false);
                if (_lastMedia?.Duration is { } d && d > TimeSpan.Zero && track.ActualWidth > 0) _media.Seek(d * Math.Clamp(e.GetPosition(track).X / track.ActualWidth, 0, 1));
            };
            track.LostMouseCapture += (_, _) => knob(false);
            timeline = (track, fill);
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(track);
            if (times)
            {
                posText = W("0:00", 10.5, null, Dim); lenText = W("", 10.5, null, Dim);
                var t = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
                DockPanel.SetDock(lenText, Dock.Right); t.Children.Add(lenText); t.Children.Add(posText);
                Grid.SetRow(t, 1); g.Children.Add(t);
            }
            return g;
        }

        switch (size)
        {
            case WidgetSize.Small:
            {
                // the cover fills the card; the bottom is frosted glass made of the same cover
                body.Margin = new Thickness(0);
                var frost = new Grid { ClipToBounds = true, VerticalAlignment = VerticalAlignment.Bottom, Height = 56 };
                var frostPic = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Bottom, Effect = new BlurEffect { Radius = 18, RenderingBias = RenderingBias.Performance } };
                frost.Children.Add(frostPic);
                frost.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x70, 0x10, 0x10, 0x14)) });
                frost.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) });
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0), Children = { title, artist } };
                var row = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
                play.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(play, Dock.Right);
                row.Children.Add(play); row.Children.Add(text);
                frost.Children.Add(row);
                body.Children.Add(cover);
                body.Children.Add(frost);
                cover.Tag = frostPic;   // the frost follows the cover
                break;
            }
            case WidgetSize.Medium:
            {
                var viz = MusicViz(size);
                double side = viz?.Kind == Views.VizStyle.Ring ? 104 : 124;
                var art = record = WidgetRecord(side);
                art.VerticalAlignment = VerticalAlignment.Center;
                AddViz(viz, root, art, side, 30);
                switcher = Switcher();
                var right = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                // the bars sit beside the artist: under it they pushed the buttons off the card
                bars = WidgetBars(16); bars.Height = 14; bars.Margin = new Thickness(10, 0, 0, 2); bars.VerticalAlignment = VerticalAlignment.Bottom;
                var artistRow = new DockPanel(); DockPanel.SetDock(bars, Dock.Right); artistRow.Children.Add(bars); artistRow.Children.Add(artist);
                right.Children.Add(switcher); right.Children.Add(title); right.Children.Add(artistRow);
                var tl = Timeline(times: false); tl.Margin = new Thickness(0, 6, 0, 2);
                right.Children.Add(tl);
                var c = Controls(36); c.HorizontalAlignment = HorizontalAlignment.Left; c.Margin = new Thickness(-2, 2, 0, 0);
                right.Children.Add(c);
                var artBox = WithRing(art, viz, side);
                var dock = new DockPanel(); DockPanel.SetDock(artBox, Dock.Left); dock.Children.Add(artBox); dock.Children.Add(right);
                body.Children.Add(dock);
                break;
            }
            default:
            {
                // the large one: a smaller cover, and the song's words under it (3–5 lines)
                var viz = MusicViz(size);
                double side = viz?.Kind == Views.VizStyle.Ring ? 84 : 100;
                var art = record = WidgetRecord(side);
                AddViz(viz, root, art, side, 40);
                switcher = Switcher();
                title.TextWrapping = TextWrapping.Wrap; title.MaxHeight = 46;
                bars = WidgetBars(); bars.Margin = new Thickness(0, 6, 0, 0);
                var info = new StackPanel { Margin = new Thickness(14, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { switcher, title, artist, bars } };
                var artBox = WithRing(art, viz, side);
                // the equaliser slides up over the card
                _musicCard = root;
                var eqKey = WKey(Glyph("\uE9E9", 13), Solid(0xFF, 0xFF, 0xFF, 0x22), null, () => ToggleEqPanel(root), 30, 30, 15, name: L.T("Эквалайзер"));
                eqKey.VerticalAlignment = VerticalAlignment.Top; eqKey.Margin = new Thickness(6, 0, 0, 0); eqKey.ToolTip = L.T("Эквалайзер");
                var head = new DockPanel(); DockPanel.SetDock(artBox, Dock.Left); DockPanel.SetDock(eqKey, Dock.Right);
                head.Children.Add(artBox); head.Children.Add(eqKey); head.Children.Add(info);
                var words = NewMusicLyrics(); words.Margin = new Thickness(2, 8, 0, 2);
                var tl = Timeline(times: true); tl.Margin = new Thickness(0, 2, 0, 0);
                var c = Controls(42); c.HorizontalAlignment = HorizontalAlignment.Center; c.Margin = new Thickness(0, 2, 0, 0);
                // the playing app's volume, as a slider
                // jelly, in white on the cover's colour; while the card is being arranged it leaves the press to the card
                var vtrack = new Views.JellySlider
                {
                    Minimum = 0, Maximum = 1, Value = PlayerVolume(), VerticalAlignment = VerticalAlignment.Center,
                    JellyBrush = Brushes.White, TrackBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
                    LabelBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x20)), CanDrag = () => !_widgetsEditing,
                };
                vtrack.ValueChanged += (_, e) => { if (vtrack.IsMouseCaptured) SetPlayerVolume((float)e.NewValue); };
                var lo = Glyph("", 11, Dim); lo.Margin = new Thickness(0, 0, 8, 0);
                var hi = Glyph("", 13, Dim); hi.Margin = new Thickness(8, 0, 0, 0);
                var vol = new DockPanel { Margin = new Thickness(18, 4, 18, 0) };
                DockPanel.SetDock(lo, Dock.Left); DockPanel.SetDock(hi, Dock.Right);
                vol.Children.Add(lo); vol.Children.Add(hi); vol.Children.Add(vtrack);
                var stack = new Grid();
                foreach (var (el, star) in new (FrameworkElement, bool)[] { (head, false), (words, true), (tl, false), (c, false), (vol, false) })
                {
                    stack.RowDefinitions.Add(new RowDefinition { Height = star ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
                    Grid.SetRow(el, stack.RowDefinitions.Count - 1);
                    stack.Children.Add(el);
                }
                body.Children.Add(stack);
                root.Tag = (Action)(() => { if (!vtrack.IsMouseCaptured) vtrack.Value = PlayerVolume(); });
                break;
            }
        }

        // the mouse wheel: the playing app's own volume, with a thin level on the right edge for a moment
        var level = new Grid { Width = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Opacity = 0, IsHitTestVisible = false };
        level.SetBinding(HeightProperty, new System.Windows.Data.Binding("ActualHeight") { Source = root, Converter = new ScaleConverter(0.62) });
        level.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) });
        var levelFill = new Border { CornerRadius = new CornerRadius(2), Background = Brushes.White, VerticalAlignment = VerticalAlignment.Bottom };
        level.Children.Add(levelFill);
        root.Children.Add(level);
        var hide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        hide.Tick += (_, _) => { hide.Stop(); level.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 300))); };
        root.MouseWheel += (_, e) =>
        {
            if (_widgetsEditing) return;
            e.Handled = true;
            float v = Math.Clamp(PlayerVolume() + (e.Delta > 0 ? 0.04f : -0.04f), 0, 1);
            SetPlayerVolume(v);
            levelFill.Height = level.ActualHeight * v;
            level.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 120)));
            hide.Stop(); hide.Start();
        };

        object? shownArt = "none";
        Color? shownMood = null;
        return () =>
        {
            var m = _lastMedia;
            title.Text = m?.Title is { Length: > 0 } t ? t : L.T("Ничего не играет");
            artist.Text = m?.Artist ?? "";
            record?.SetPlaying(m?.IsPlaying == true);
            bars?.SetPlaying(m?.IsPlaying == true);
            playIcon.Text = m?.IsPlaying == true ? "" : "";
            if (appName != null && switcher != null)
            {
                var sessions = MediaSessions();
                appName.Text = (m?.SourceApp ?? "").ToUpperInvariant();
                // the switcher only says "more" when there is another app to switch to
                if (((StackPanel)switcher.Child).Children[1] is FrameworkElement arrows) arrows.Visibility = sessions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                switcher.IsHitTestVisible = sessions.Count > 1;
            }
            if (!ReferenceEquals(shownArt, m?.Art))
            {
                shownArt = m?.Art;
                CoverSwap(cover, m?.Art);
                record?.SetCover(m?.Art);
                if (cover.Tag is Image frostPic) frostPic.Source = m?.Art;
                var dominant = m?.Art != null ? ColorTools.Dominant(m.Art) : Color.FromRgb(0x9A, 0x86, 0xFF);
                SetVizColor(dominant);
                var c = m?.Art != null ? ColorTools.DarkForWhiteText(dominant) : Color.FromRgb(0x2E, 0x26, 0x48);
                if (shownMood != c)
                {
                    shownMood = c;
                    mood.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
            }
            if (record != null)
            {
                var d0 = m?.Duration ?? TimeSpan.Zero;
                record.SetProgress(d0 > TimeSpan.Zero ? (m!.CurrentPosition / d0) : 0);
            }
            if (timeline is { } tl0 && !tl0.Track.IsMouseCaptured)
            {
                var (track, fill) = tl0;
                var d = m?.Duration ?? TimeSpan.Zero; var p = m?.CurrentPosition ?? TimeSpan.Zero;
                if (d > TimeSpan.Zero && p > d) p = d;
                fill.Width = d > TimeSpan.Zero && track.ActualWidth > 0 ? track.ActualWidth * Math.Clamp(p / d, 0, 1) : 0;
                if (posText != null && lenText != null)
                {
                    posText.Text = $"{(int)p.TotalMinutes}:{p.Seconds:00}";
                    lenText.Text = d > TimeSpan.Zero ? $"-{(int)(d - p).TotalMinutes}:{(d - p).Seconds:00}" : "";
                }
            }
            if (root.Tag is Action volumeRefresh) volumeRefresh();
            if (size == WidgetSize.Large) TickMusicLyrics();
        };
    }

    /// <summary>Wave or bars: along the bottom edge of the card, under the content.</summary>
    /// <summary>The cover as a record (see Views.MusicRecordView), in the widget's white-on-colour look.</summary>
    private Views.MusicRecordView WidgetRecord(double side)
    {
        var r = new Views.MusicRecordView
        {
            Width = side, Height = side,
            CenterBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0x1A, 0x1A, 0x20)),
            GlyphBrush = Brushes.White,
            TrackBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            ProgressBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
        };
        r.Toggled += () => { if (!_widgetsEditing) _media.TogglePlayPause(); };
        return r;
    }

    private static Views.MusicBarsView WidgetBars(int count = 24) => new(count) { HorizontalAlignment = HorizontalAlignment.Left, BarBrush = Brushes.White };

    private static void AddViz(Views.VisualizerView? viz, Grid root, FrameworkElement art, double side, double height)
    {
        if (viz == null || viz.Kind == Views.VizStyle.Ring) return;
        viz.Height = height;
        viz.VerticalAlignment = VerticalAlignment.Bottom;
        root.Children.Insert(1, viz);
    }

    /// <summary>Ring: rays around the cover (the cover a little smaller to make room).</summary>
    private static FrameworkElement WithRing(FrameworkElement art, Views.VisualizerView? viz, double side)
    {
        if (viz?.Kind != Views.VizStyle.Ring) return art;
        var box = new Grid { Width = side + 20, Height = side + 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-10, 0, 0, 0) };
        viz.InnerRadius = side / 2 + 2;
        box.Children.Add(viz);
        art.HorizontalAlignment = HorizontalAlignment.Center; art.VerticalAlignment = VerticalAlignment.Center;
        box.Children.Add(art);
        return box;
    }

    /// <summary>The playing app's volume (its own audio session, or the speakers' when it has none).</summary>
    private float _demoPlayerVolume = 0.7f;
    /// <summary>The large music widget's card (the equaliser slides over it).</summary>
    private Grid? _musicCard;
    private float PlayerVolume() => Settings.ReadOnly ? _demoPlayerVolume : Math.Clamp(_audio.GetVolume(_lastMedia?.AppId ?? "").Volume, 0, 1);
    private void SetPlayerVolume(float v) { if (Settings.ReadOnly) _demoPlayerVolume = v; else _audio.SetVolume(_lastMedia?.AppId ?? "", v); }

    /// <summary>A new cover fades in over the old one (the old one goes once it is covered).</summary>
    private static void CoverSwap(Grid host, BitmapSource? art)
    {
        UIElement next = art != null
            ? new Image { Source = art, Stretch = Stretch.UniformToFill }
            : new Border
            {
                Background = new LinearGradientBrush(Color.FromRgb(0x5E, 0x5C, 0xE6), Color.FromRgb(0xBF, 0x5A, 0xF2), 45),
                Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 30, Foreground = Brushes.White, Opacity = 0.8,
                                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
        RenderOptions.SetBitmapScalingMode(next, BitmapScalingMode.HighQuality);
        host.Children.Add(next);
        if (host.Children.Count == 1 || Motion.Reduced) { while (host.Children.Count > 1) host.Children.RemoveAt(0); return; }
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (_, _) => { while (host.Children.Count > 1) host.Children.RemoveAt(0); };
        next.BeginAnimation(OpacityProperty, fade);
    }

    private sealed class ScaleConverter(double k) : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) => value is double d ? d * k : 0.0;
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
    }
}

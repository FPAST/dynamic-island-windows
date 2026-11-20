using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// The photo widget: one photo, a folder as a slideshow, or "this day" — photos taken on today's date in earlier
/// years. Each picture is framed around its focus point (set by a click in the editor, or found from the faces in
/// it), zooms in very slowly while shown and crossfades to the next. A file or folder dropped on the widget
/// becomes its source. Pictures are decoded at the widget's size only.
/// </summary>
public partial class MainWindow
{
    private static List<string>? _photoList;
    private static string _photoListKey = "";
    private static bool _photoScanning, _photoNoMemories;
    private static int _photoIndex;
    private static DateTime _photoTurned = DateTime.Now;
    private static readonly Dictionary<string, (DateTime Taken, bool Exif)> _photoDates = new();
    private static readonly Dictionary<string, Point?> _photoFaces = new();
    /// <summary>Tests: the folder of made-up pictures and their dates (screenshots never show the user's photos).</summary>
    private string? _photoDemoFolder;
    private Dictionary<string, DateTime>? _photoDemoDates;

    private string PhotoFolder => _photoDemoFolder ?? (_settings.PhotoFolder is { Length: > 0 } f && Directory.Exists(f) ? f : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

    private string PhotoKey => $"{_settings.PhotoMode}|{PhotoFolder}|{_settings.PhotoSubfolders}|{_settings.PhotoShuffle}|{_settings.PhotoFile}|{DateTime.Today:yyyyMMdd}";

    /// <summary>The pictures to show (found in the background; null while looking).</summary>
    private List<string>? PhotoList()
    {
        if (Settings.ReadOnly && _photoDemoFolder == null) return [];   // demo videos: never the user's own photos
        var key = PhotoKey;
        if (key == _photoListKey && _photoList != null) return _photoList;
        if (_photoScanning) return null;
        _photoScanning = true;
        string mode = _settings.PhotoMode, folder = PhotoFolder, single = _settings.PhotoFile;
        bool sub = _settings.PhotoSubfolders, shuffle = _settings.PhotoShuffle;
        var demoDates = _photoDemoDates;
        Task.Run(() =>
        {
            if (mode == "single") return (File.Exists(single) ? [single] : new List<string>(), false);
            var files = PhotoLibrary.Scan(folder, sub);
            if (mode == "thisday")
            {
                var dated = files.Select(f => (f, demoDates?.GetValueOrDefault(f) ?? Taken(f))).ToList();
                var today = PhotoLibrary.ThisDay(dated, DateTime.Today);
                if (today.Count > 0) return (today, false);
                return (PhotoLibrary.Order(files, shuffle, DateTime.Today.DayOfYear), true);   // no memories today: the folder instead
            }
            return (PhotoLibrary.Order(files, shuffle, DateTime.Today.DayOfYear), false);
        }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            _photoScanning = false;
            if (t.IsFaulted) { Log.Write("photos: " + t.Exception?.GetBaseException().Message); _photoList = []; }
            else (_photoList, _photoNoMemories) = t.Result;
            _photoListKey = key; _photoIndex = 0; _photoTurned = DateTime.Now;
            TickWidgets();
        }));
        return null;
    }

    private DateTime Taken(string path)
    {
        if (_photoDemoDates?.TryGetValue(path, out var demo) == true) return demo;
        lock (_photoDates)
            if (_photoDates.TryGetValue(path, out var known)) return known.Taken;
        var d = PhotoLibrary.TakenOn(path, out bool exif);
        lock (_photoDates) _photoDates[path] = (d, exif);
        return d;
    }

    private TimeSpan PhotoInterval => TimeSpan.FromMinutes(Math.Clamp(_settings.PhotoIntervalMin, 1, 1440));

    /// <summary>The focus point of a picture: the user's click, else the faces in it, else the middle.</summary>
    private Point PhotoFocus(string file, Action whenFacesKnown)
    {
        if (_settings.PhotoFocus.TryGetValue(file, out var f) && f.Length == 2) return new Point(f[0], f[1]);
        lock (_photoFaces)
        {
            if (_photoFaces.TryGetValue(file, out var faces)) return faces ?? new Point(0.5, 0.45);
            _photoFaces[file] = null;
        }
        _ = PhotoLibrary.DetectFacesAsync(file).ContinueWith(t =>
        {
            if (t.Result is not { } p) return;
            lock (_photoFaces) _photoFaces[file] = p;
            Dispatcher.BeginInvoke(whenFacesKnown);
        });
        return new Point(0.5, 0.45);
    }

    private Action PhotosWidget(Grid body, Border wash, bool big)
    {
        var size = big ? WidgetSize.Large : _buildSize;
        var root = (Grid)body.Parent;
        body.Margin = new Thickness(0);
        var host = new Grid { ClipToBounds = true };
        body.Children.Add(host);
        // a thin glass rim in the photo's own colour
        double radius = Window.GetWindow(root) is Views.DesktopWidgetWindow w0 ? w0.Card.CornerRadius : 22;
        var rim = new Border { BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(radius), IsHitTestVisible = false, BorderBrush = Solid(0xFF, 0xFF, 0xFF, 0x40) };
        body.Children.Add(rim);
        var empty = W("", 13, FontWeights.SemiBold, Brushes.White); empty.TextWrapping = TextWrapping.Wrap; empty.TextAlignment = TextAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center; empty.Margin = new Thickness(16); empty.Visibility = Visibility.Collapsed;
        body.Children.Add(empty);

        // the memory caption ("3 года назад") stays; the date and the buttons come with the pointer
        var memory = W("", 12.5, FontWeights.SemiBold, Brushes.White);
        var memoryPlate = new Border { Child = memory, Padding = new Thickness(9, 3, 9, 4), CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)),
                                       HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12), Visibility = Visibility.Collapsed };
        body.Children.Add(memoryPlate);
        var date = W("", 12, FontWeights.SemiBold, Brushes.White); date.VerticalAlignment = VerticalAlignment.Center; date.TextTrimming = TextTrimming.CharacterEllipsis;
        var nextKey = WKey(Glyph("", 12, Brushes.White), new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), null, () => TurnPhoto(1), 30, 30, 15, name: L.T("Следующее фото"));
        var openKey = WKey(Glyph("", 12, Brushes.White), new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), null, OpenPhoto, 30, 30, 15, name: L.T("Открыть файл"));
        openKey.Margin = new Thickness(6, 0, 0, 0);
        var bar = new DockPanel { Margin = new Thickness(12, 0, 12, 10), VerticalAlignment = VerticalAlignment.Bottom };
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Children = { nextKey, openKey } };
        DockPanel.SetDock(keys, Dock.Right); bar.Children.Add(keys); bar.Children.Add(date);
        var shade = new Grid { Opacity = 0, IsHitTestVisible = true, Background = Brushes.Transparent };
        shade.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Bottom, Height = 64, IsHitTestVisible = false, Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0x90, 0, 0, 0), 90) });
        shade.Children.Add(bar);
        body.Children.Add(shade);
        void Hover(bool on) => shade.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 200)));
        root.MouseEnter += (_, _) => Hover(true);
        root.MouseLeave += (_, _) => Hover(false);
        if (Settings.ReadOnly && _photoDemoFolder != null && size == WidgetSize.Large) shade.Opacity = 1;   // screenshots: the hover bar is part of the picture

        // a file or a folder dropped on the widget becomes its source
        root.AllowDrop = true;
        root.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
            var p = paths[0];
            if (Directory.Exists(p)) { _settings.PhotoMode = _settings.PhotoMode == "thisday" ? "thisday" : "folder"; _settings.PhotoFolder = p; }
            else if (PhotoLibrary.IsPicture(p)) { _settings.PhotoMode = "single"; _settings.PhotoFile = p; }
            else return;
            if (!Settings.ReadOnly) _settings.Save();
            _photoListKey = "";
            TickWidgets();
        };
        root.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };

        Border? current = null;
        string? shownFile = null;
        BitmapSource? shownBitmap = null;
        int decodeWidth = size switch { WidgetSize.Small => 480, WidgetSize.Medium => 860, _ => 960 };
        int loading = 0;

        void Frame(Border layer, BitmapSource bmp, string file, bool zoom)
        {
            double vw = host.ActualWidth, vh = host.ActualHeight;
            if (vw <= 0 || vh <= 0) return;
            var focus = PhotoFocus(file, () => { if (ReferenceEquals(layer, current)) Frame(layer, bmp, file, false); });
            var crop = PhotoLibrary.Crop(bmp.PixelWidth, bmp.PixelHeight, vw, vh, focus.X, focus.Y);
            layer.Background = new ImageBrush(bmp) { Viewbox = crop, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill };
            // a very slow zoom (1.0 → 1.06 over the whole showing) towards the focus point
            layer.RenderTransformOrigin = new Point(Math.Clamp((focus.X - crop.X) / crop.Width, 0, 1), Math.Clamp((focus.Y - crop.Y) / crop.Height, 0, 1));
            if (!zoom) return;
            var scale = new ScaleTransform(1, 1);
            layer.RenderTransform = scale;
            if (_settings.PhotoKenBurns && !Motion.Reduced && _photoList is { Count: > 1 })
            {
                var grow = new DoubleAnimation(1, 1.06, PhotoInterval) { FillBehavior = FillBehavior.HoldEnd };
                Timeline.SetDesiredFrameRate(grow, 4);   // it moves a fraction of a pixel a second: a few frames are plenty
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
        }
        host.SizeChanged += (_, _) => { if (current != null && shownBitmap != null && shownFile != null) Frame(current, shownBitmap, shownFile, false); };
        root.Loaded += (_, _) =>
        {
            if (Window.GetWindow(root) is not Views.DesktopWidgetWindow win) return;
            win.PausedChanged += paused =>
            {
                if (current?.RenderTransform is not ScaleTransform st) return;
                double now = st.ScaleX;
                st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                st.ScaleX = st.ScaleY = now;
                if (paused || !_settings.PhotoKenBurns || Motion.Reduced || now >= 1.06) return;
                var left = PhotoInterval - (DateTime.Now - _photoTurned);
                var grow = new DoubleAnimation(now, 1.06, left > TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1)) { FillBehavior = FillBehavior.HoldEnd };
                Timeline.SetDesiredFrameRate(grow, 4);
                st.BeginAnimation(ScaleTransform.ScaleXProperty, grow); st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            };
        };

        void Show(BitmapSource bmp, string file)
        {
            var layer = new Border { Opacity = 0 };
            host.Children.Add(layer);
            Frame(layer, bmp, file, true);
            var old = current;
            current = layer; shownFile = file; shownBitmap = bmp;
            var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(Motion.Reduced ? 1 : 700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            if (old != null) fade.Completed += (_, _) => host.Children.Remove(old);
            layer.BeginAnimation(OpacityProperty, fade);
            var c = ColorTools.Dominant(bmp);
            rim.BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, (byte)(c.R + (255 - c.R) * 0.4), (byte)(c.G + (255 - c.G) * 0.4), (byte)(c.B + (255 - c.B) * 0.4)));
        }

        string shownKey = "";
        return () =>
        {
            var list = PhotoList();
            if (list == null) return;   // still looking
            if (list.Count == 0 && Settings.ReadOnly && _photoDemoFolder == null)
            {
                wash.Background = DemoPhoto(_photoIndex);   // demo videos
                return;
            }
            empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (list.Count == 0)
            {
                empty.Text = _settings.PhotoMode == "single" ? L.T("Перетащите фото на виджет") : L.T("Здесь нет фото — перетащите папку или фото на виджет");
                return;
            }
            if (list.Count > 1 && DateTime.Now - _photoTurned > PhotoInterval) { _photoIndex++; _photoTurned = DateTime.Now; }
            var file = list[((_photoIndex % list.Count) + list.Count) % list.Count];
            bool memories = _settings.PhotoMode == "thisday" && !_photoNoMemories;
            memoryPlate.Visibility = memories || _settings.PhotoMode == "thisday" ? Visibility.Visible : Visibility.Collapsed;
            var key = file + "|" + _photoListKey;
            if (key == shownKey) return;
            shownKey = key;
            int ticket = ++loading;
            _ = Task.Run(() =>
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.UriSource = new Uri(file); bmp.DecodePixelWidth = decodeWidth; bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; bmp.EndInit(); bmp.Freeze();
                    return ((BitmapSource?)bmp, Taken(file));
                }
                catch (Exception e) { Log.Write($"photos: {Path.GetFileName(file)}: {e.Message}"); return ((BitmapSource?)null, DateTime.MinValue); }
            }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                if (ticket != loading || t.Result.Item1 is not { } bmp) return;
                Show(bmp, file);
                var taken = t.Result.Item2;
                date.Text = taken == DateTime.MinValue ? Path.GetFileName(file) : taken.ToString("d MMMM yyyy", Loc.Culture);
                memory.Text = memories ? PhotoLibrary.YearsAgo(taken, DateTime.Today) : L.T("Сегодня воспоминаний нет");
            }));
        };
    }

    private void TurnPhoto(int by)
    {
        _photoIndex += by; _photoTurned = DateTime.Now;
        TickWidgets();
    }

    private void OpenPhoto()
    {
        var list = _photoList;
        if (Settings.ReadOnly || list is not { Count: > 0 }) return;
        var file = list[((_photoIndex % list.Count) + list.Count) % list.Count];
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
        catch (Exception e) { Log.Write("photos: open: " + e.Message); ShowToast("", L.T("Не получилось открыть фото"), e.Message); }
    }

    /// <summary>The picture shown now (for the focus picker in the editor).</summary>
    private string? CurrentPhoto() =>
        _photoList is { Count: > 0 } l ? l[((_photoIndex % l.Count) + l.Count) % l.Count] : null;

    /// <summary>
    /// Tests: made-up landscape pictures (sky, hills, a bright "subject" off to one side), written as JPEGs with
    /// their dates; the first is wide with the subject far right, to show the focus point at work.
    /// </summary>
    private static Dictionary<string, DateTime> MakeDemoPhotos(string folder)
    {
        Directory.CreateDirectory(folder);
        var dates = new Dictionary<string, DateTime>();
        var today = DateTime.Today;
        var sets = new (string Name, int W, int H, Color Sky, Color Ground, double SubjectX, DateTime Taken)[]
        {
            ("a-wide.jpg", 2400, 1000, Color.FromRgb(0x5B, 0x8F, 0xD8), Color.FromRgb(0x3E, 0x7A, 0x3A), 0.86, today.AddYears(-3)),
            ("b-sunset.jpg", 1600, 1100, Color.FromRgb(0xF2, 0x8A, 0x4B), Color.FromRgb(0x5A, 0x2E, 0x3E), 0.3, today.AddYears(-7)),
            ("c-sea.jpg", 1600, 1000, Color.FromRgb(0x7F, 0xC8, 0xE8), Color.FromRgb(0x1F, 0x6E, 0x9C), 0.6, today.AddDays(-40)),
        };
        foreach (var s in sets)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(s.Sky, Color.FromRgb((byte)(s.Sky.R / 2 + 120), (byte)(s.Sky.G / 2 + 110), (byte)(s.Sky.B / 2 + 100)), 90), null, new Rect(0, 0, s.W, s.H));
                var hills = new StreamGeometry();
                using (var g = hills.Open())
                {
                    g.BeginFigure(new Point(0, s.H * 0.68), true, true);
                    g.BezierTo(new Point(s.W * 0.3, s.H * 0.55), new Point(s.W * 0.6, s.H * 0.8), new Point(s.W, s.H * 0.62), true, true);
                    g.LineTo(new Point(s.W, s.H), true, false); g.LineTo(new Point(0, s.H), true, false);
                }
                dc.DrawGeometry(new SolidColorBrush(s.Ground), null, hills);
                // the subject: a bright figure (sun, lighthouse, balloon)
                var c = new Point(s.W * s.SubjectX, s.H * 0.42);
                dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(0xFF, 0xF4, 0xC8), Color.FromArgb(0, 0xFF, 0xE0, 0x80)), null, c, s.H * 0.2, s.H * 0.2);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A)), null, c, s.H * 0.07, s.H * 0.07);
            }
            var rtb = new RenderTargetBitmap(s.W, s.H, 96, 96, PixelFormats.Pbgra32); rtb.Render(dv);
            var enc = new JpegBitmapEncoder { QualityLevel = 85 }; enc.Frames.Add(BitmapFrame.Create(rtb));
            var path = Path.Combine(folder, s.Name);
            using (var fs = File.Create(path)) enc.Save(fs);
            dates[path] = s.Taken.AddHours(15);
        }
        return dates;
    }

    /// <summary>Pretend photos for demo pictures (never the user's own).</summary>
    private static Brush DemoPhoto(int i)
    {
        var sets = new[] { (0xFF5E3A, 0x5856D6), (0x34C759, 0x007AFF), (0xFF9500, 0xFF2D55) };
        var (a, b) = sets[((i % 3) + 3) % 3];
        static Color C(int v) => Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return new RadialGradientBrush(C(a), C(b)) { Center = new Point(0.3, 0.3), GradientOrigin = new Point(0.3, 0.3), RadiusX = 1, RadiusY = 1 };
    }
}

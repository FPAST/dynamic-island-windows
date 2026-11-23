using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>The big, opened views of the widgets (a click on a widget on the desktop opens it).</summary>
public partial class MainWindow
{
    private Action BuildExpanded(string id, Grid body, Border wash) => id switch
    {
        "music" => MusicBig(body, wash),
        "weather" => WeatherBig(body, wash),
        "calendar" or "month" => MonthWidget(body, big: true),
        "notes" => NotesBig(body),
        "todo" => TodoWidget(body, compact: false),
        "clipboard" => ClipboardBig(body),
        "reminders" => RemindersBig(body),
        "system" => SystemBig(body),
        "top" => TopWidget(body, 10),
        "timer" => TimerOpen(body),
        "stopwatch" => StopwatchWidget(body, big: true),
        "worldclock" => WorldClockBig(body),
        "photos" => PhotosWidget(body, wash, big: true),
        "launcher" => LauncherWidget(body, 30),
        "countdown" => CountdownEditor(body, wash),
        _ => BuildWidget(id, body, wash),
    };

    /// <summary>Puts several parts under each other, each in its own grid; refreshes them all.</summary>
    private static (StackPanel Stack, Func<double, Grid> Part) Parts()
    {
        var stack = new StackPanel();
        return (stack, h => { var g = new Grid { Height = h, Margin = new Thickness(0, 0, 0, 12) }; stack.Children.Add(g); return g; });
    }

    // ------------------------------------------------------------------ music

    private Action MusicBig(Grid body, Border wash)
    {
        LightText(body);
        var art = new Border { Width = 132, Height = 132, CornerRadius = new CornerRadius(18), Background = Solid(0xFF, 0xFF, 0xFF, 0x26),
                               Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.45 } };
        var app = W("", 11, FontWeights.SemiBold, Dim);
        var title = W("", 19, FontWeights.SemiBold); title.TextWrapping = TextWrapping.Wrap; title.MaxHeight = 52;
        var artist = W("", 14, null, Dim);
        var info = new StackPanel { Margin = new Thickness(16, 6, 30, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(app); info.Children.Add(title); info.Children.Add(artist);
        var head = new DockPanel(); DockPanel.SetDock(art, Dock.Left); head.Children.Add(art); head.Children.Add(info);

        // the progress bar: click to jump there
        var (track, fill) = Bar(5, Brushes.White);
        track.Cursor = Cursors.Hand; track.Background = Brushes.Transparent; track.Margin = new Thickness(0, 12, 0, 0);
        var seekKnob = GlassKnob(track, fill, 5);
        // drag (or click) to seek: the knob turns to glass while held, the time jumps on release
        track.MouseLeftButtonDown += (_, e) => { e.Handled = true; track.CaptureMouse(); seekKnob(true); fill.Width = Math.Clamp(e.GetPosition(track).X, 0, track.ActualWidth); };
        track.MouseMove += (_, e) => { if (track.IsMouseCaptured) fill.Width = Math.Clamp(e.GetPosition(track).X, 0, track.ActualWidth); };
        track.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!track.IsMouseCaptured) return;
            track.ReleaseMouseCapture(); seekKnob(false);
            if (_lastMedia?.Duration is { } d && d > TimeSpan.Zero && track.ActualWidth > 0) _ = _media.Seek(d * Math.Clamp(e.GetPosition(track).X / track.ActualWidth, 0, 1));
        };
        var pos = W("0:00", 11, null, Dim); var len = W("", 11, null, Dim);
        var times = new DockPanel { Margin = new Thickness(0, 5, 0, 0) }; DockPanel.SetDock(len, Dock.Right); times.Children.Add(len); times.Children.Add(pos);

        var playIcon = Glyph("", 22);
        // the three playback controls share one glass capsule
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var prev = WKey(Glyph("\uE892", 18), Brushes.Transparent, null, () => _ = _media.Previous(), 52, 52, name: L.T("Предыдущий трек"));
        var play = WKey(playIcon, Solid(0xFF, 0xFF, 0xFF, 0x30), null, () => _ = _media.TogglePlayPause(), 60, 52, 26, name: L.T("Пауза или воспроизведение"));
        var next = WKey(Glyph("\uE893", 18), Brushes.Transparent, null, () => _ = _media.Next(), 52, 52, name: L.T("Следующий трек"));
        play.Margin = new Thickness(10, 0, 10, 0);
        row.Children.Add(prev); row.Children.Add(play); row.Children.Add(next);
        var controls = GlassGroup(row, 32);
        controls.HorizontalAlignment = HorizontalAlignment.Center; controls.Margin = new Thickness(0, 8, 0, 0);

        // the volume of the playing app (or of the speakers)
        var (vtrack, vfill) = Bar(5, Brushes.White);
        vtrack.Cursor = Cursors.Hand; vtrack.Background = Brushes.Transparent;
        float demoVol = 0.7f;
        void SetVol(MouseEventArgs e)
        {
            float v = (float)Math.Clamp(e.GetPosition(vtrack).X / Math.Max(1, vtrack.ActualWidth), 0, 1);
            if (Settings.ReadOnly) demoVol = v; else _audio.SetVolume(_lastMedia?.AppId ?? "", v);
            vfill.Width = vtrack.ActualWidth * v;
        }
        var volKnob = GlassKnob(vtrack, vfill, 5);
        vtrack.MouseLeftButtonDown += (_, e) => { e.Handled = true; vtrack.CaptureMouse(); volKnob(true); SetVol(e); };
        vtrack.MouseMove += (_, e) => { if (vtrack.IsMouseCaptured) SetVol(e); };
        vtrack.MouseLeftButtonUp += (_, e) => { e.Handled = true; vtrack.ReleaseMouseCapture(); volKnob(false); };
        var lo = Glyph("", 12, Dim); lo.Margin = new Thickness(0, 0, 10, 0);
        var hi = Glyph("", 14, Dim); hi.Margin = new Thickness(10, 0, 0, 0);
        var vol = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(lo, Dock.Left); DockPanel.SetDock(hi, Dock.Right);
        vol.Children.Add(lo); vol.Children.Add(hi); vol.Children.Add(vtrack);

        var stack = new StackPanel(); stack.Children.Add(head); stack.Children.Add(track); stack.Children.Add(times); stack.Children.Add(controls); stack.Children.Add(vol);
        body.Children.Add(stack);
        object? shownArt = "none";
        return () =>
        {
            var m = _lastMedia;
            title.Text = m?.Title is { Length: > 0 } t ? t : L.T("Ничего не играет");
            artist.Text = m?.Artist ?? "";
            app.Text = (m?.SourceApp ?? "").ToUpperInvariant();
            if (!ReferenceEquals(shownArt, m?.Art))
            {
                shownArt = m?.Art;
                art.Background = m?.Art != null ? new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill } : Solid(0xFF, 0xFF, 0xFF, 0x26);
                wash.Background = m?.Art != null ? new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill, Opacity = 0.7 } : new LinearGradientBrush(Color.FromArgb(0x70, 0xFF, 0x37, 0x5F), Color.FromArgb(0x40, 0xBF, 0x5A, 0xF2), 45);
                wash.Effect = m?.Art != null ? new BlurEffect { Radius = 60 } : null;
            }
            playIcon.Text = m?.IsPlaying == true ? "" : "";
            var d = m?.Duration ?? TimeSpan.Zero; var p = m?.CurrentPosition ?? TimeSpan.Zero;
            if (d > TimeSpan.Zero && p > d) p = d;
            pos.Text = $"{(int)p.TotalMinutes}:{p.Seconds:00}"; len.Text = d > TimeSpan.Zero ? $"-{(int)(d - p).TotalMinutes}:{(d - p).Seconds:00}" : "";
            if (!track.IsMouseCaptured) fill.Width = d > TimeSpan.Zero && track.ActualWidth > 0 ? track.ActualWidth * Math.Clamp(p / d, 0, 1) : 0;
            if (!vtrack.IsMouseCaptured && vtrack.ActualWidth > 0)
                vfill.Width = vtrack.ActualWidth * (Settings.ReadOnly ? demoVol : Math.Clamp(_audio.GetVolume(m?.AppId ?? "").Volume, 0, 1));
        };
    }

    // ------------------------------------------------------------------ weather

    private Action WeatherBig(Grid body, Border wash)
    {
        LightText(body);
        var place = W("", 16, FontWeights.SemiBold);
        var temp = W("—", 64, FontWeights.Thin, null, big: true); temp.Margin = new Thickness(-3, -8, 0, -6);
        var desc = W("", 14, FontWeights.SemiBold); var hl = W("", 13, FontWeights.SemiBold, Dim);
        var icon = Glyph("", 40, null, symbol: true); icon.HorizontalAlignment = HorizontalAlignment.Right; icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(0, 26, 34, 0);
        var head = new StackPanel(); head.Children.Add(place); head.Children.Add(temp); head.Children.Add(desc); head.Children.Add(hl);
        var advice = W("", 14, FontWeights.SemiBold); advice.TextWrapping = TextWrapping.Wrap; advice.Margin = new Thickness(0, 8, 0, 0);
        var staleNote = W("", 11.5, FontWeights.SemiBold, Dim);
        head.Children.Add(advice); head.Children.Add(staleNote);
        var chart = new Views.WeatherChart { Span = 24, Height = 96 };
        var chartPlate = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 8, 12, 6), Child = chart, Margin = new Thickness(0, 12, 0, 0) };
        chartPlate.SetResourceReference(Border.BackgroundProperty, "WFill");
        var sun = new Views.SunArc { Height = 70 };
        var sunCaption = W(L.T("Солнце"), 12, FontWeights.SemiBold, Dim);
        var sunPlate = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 8, 12, 6), Margin = new Thickness(0, 10, 0, 0), Child = new StackPanel { Children = { sunCaption, sun } } };
        sunPlate.SetResourceReference(Border.BackgroundProperty, "WFill");
        var hours = new UniformGrid { Columns = 6, Margin = new Thickness(0, 14, 0, 0) };
        var hoursPlate = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(6, 2, 6, 8), Child = hours, Margin = new Thickness(0, 12, 0, 0) };
        hoursPlate.SetResourceReference(Border.BackgroundProperty, "WFill");
        hours.Margin = new Thickness(0, 6, 0, 0);
        var details = new UniformGrid { Columns = 3, Rows = 2, Margin = new Thickness(0, 10, 0, 0) };
        hoursPlate.Visibility = Visibility.Collapsed;   // the chart shows the hours now
        var stack = new StackPanel(); stack.Children.Add(head); stack.Children.Add(chartPlate); stack.Children.Add(hoursPlate); stack.Children.Add(sunPlate); stack.Children.Add(details);
        body.Children.Add(stack); body.Children.Add(icon);
        object? shown = "none";
        StackPanel Tile(string label, string value)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 6, 8) };
            s.Children.Add(W(label, 12, FontWeights.SemiBold, Dim));
            s.Children.Add(W(value, 17, FontWeights.Medium));
            return s;
        }
        return () =>
        {
            var w = _lastWeather;
            if (ReferenceEquals(w, shown)) return;
            shown = w;
            wash.Background = Sky(w);
            if (w == null) { place.Text = L.T("Погода"); desc.Text = L.T("Обновляю…"); return; }
            place.Text = w.Place;
            temp.Text = $"{Math.Round(w.Temp)}°";
            icon.Text = WeatherService.Glyph(w.Code, w.IsDay);
            desc.Text = WeatherService.Describe(w.Code);
            hl.Text = L.F("Макс.: {0}°, мин.: {1}°", Math.Round(w.Max), Math.Round(w.Min));
            var placeNow = w.PlaceNow;
            advice.Text = WeatherAdvice.Build(w, placeNow);
            staleNote.Text = StaleNote(w);
            staleNote.Visibility = staleNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            chart.Show(w.Hourly, placeNow);
            chartPlate.Visibility = w.Hourly.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            sun.Show(w.Sunrise, w.Sunset, placeNow);
            hoursPlate.Visibility = w.Hourly.Count > 1 ? Visibility.Collapsed : Visibility.Visible;
            hours.Children.Clear();
            foreach (var hr in w.Hours.Where(x => x.Time > DateTime.Now).Take(12))
            {
                var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
                var c = W(hr.Time.ToString("HH"), 11, FontWeights.SemiBold, Dim); c.HorizontalAlignment = HorizontalAlignment.Center;
                var b = Glyph(WeatherService.Glyph(hr.Code, hr.IsDay), 15, null, symbol: true); b.HorizontalAlignment = HorizontalAlignment.Center; b.Margin = new Thickness(0, 2, 0, 2);
                var a = W($"{Math.Round(hr.Temp)}°", 13, FontWeights.SemiBold); a.HorizontalAlignment = HorizontalAlignment.Center;
                s.Children.Add(c); s.Children.Add(b); s.Children.Add(a);
                hours.Children.Add(s);
            }
            details.Children.Clear();
            details.Children.Add(Tile(L.T("Ощущается"), $"{Math.Round(w.FeelsLike)}°"));
            details.Children.Add(Tile(L.T("Влажность"), $"{w.Humidity:0}%"));
            details.Children.Add(Tile(L.T("Ветер"), L.F("{0:0} м/с", w.WindSpeed)));
            details.Children.Add(Tile(L.T("УФ-индекс"), $"{w.Uv:0}"));
            details.Children.Add(Tile(L.T("Давление"), L.F("{0:0} мм", w.PressureMm)));
            details.Children.Add(Tile(L.T("Осадки"), $"{w.PrecipChance:0}%"));
            if (w.Aqi is { } aqi)
            {
                details.Rows = 3;
                details.Children.Add(Tile(L.T("Воздух (AQI)"), $"{aqi:0} · {AqiWord(aqi)}"));
                if (w.Pm25 is { } pm) details.Children.Add(Tile("PM2.5", L.F("{0:0.#} мкг/м³", pm)));
            }
        };
    }

    // ------------------------------------------------------------------ notes

    private Action NotesBig(Grid body)
    {
        var accent = Solid(0xFF, 0xD6, 0x0A);
        var (box, tb) = Field(L.T("Новая заметка — Enter, чтобы сохранить"));
        box.Margin = new Thickness(0, 10, 0, 10);
        var list = new StackPanel();
        var s = new StackPanel(); s.Children.Add(Caption("", L.T("Заметки"), accent)); s.Children.Add(box); s.Children.Add(list);
        body.Children.Add(s);
        tb.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter || tb.Text.Trim().Length == 0) return;
            e.Handled = true;
            if (!Settings.ReadOnly) { NotesStore.Notes.Insert(0, new Note { Text = tb.Text.Trim(), Created = DateTime.Now }); NotesStore.Save(); }
            tb.Text = "";
            TickWidgets();
        };
        string last = "-";
        return () =>
        {
            var notes = NotesStore.Notes.Take(6).ToList();
            var key = string.Join("|", notes.Select(n => n.Text));
            if (key == last) return;
            last = key;
            list.Children.Clear();
            if (notes.Count == 0) list.Children.Add(W(L.T("Пока пусто"), 13, null, Dim));
            foreach (var n in notes)
            {
                var note = n;
                var lines = n.Text.Split('\n');
                var item = new StackPanel();
                item.Children.Add(W(lines[0].Trim(), 13.5, FontWeights.SemiBold));
                item.Children.Add(W(n.Created.ToString("d MMM, HH:mm", Loc.Culture) + (lines.Length > 1 ? "  " + lines[1].Trim() : ""), 11.5, null, Dim));
                var del = Glyph("", 12, Dim); del.Cursor = Cursors.Hand; del.Opacity = 0; del.Margin = new Thickness(8, 0, 0, 0);
                del.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (!Settings.ReadOnly) { NotesStore.Notes.Remove(note); NotesStore.Save(); } TickWidgets(); };
                var row = new DockPanel(); DockPanel.SetDock(del, Dock.Right); row.Children.Add(del); row.Children.Add(item);
                var plate = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), Child = row, Cursor = Cursors.Hand };
                plate.SetResourceReference(Border.BackgroundProperty, "WFill");
                plate.MouseEnter += (_, _) => del.Opacity = 1; plate.MouseLeave += (_, _) => del.Opacity = 0;
                plate.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenTab(TabNotes); };
                list.Children.Add(plate);
            }
        };
    }

    // ------------------------------------------------------------------ clipboard, reminders

    private Action ClipboardBig(Grid body)
    {
        var accent = Solid(0x64, 0xD2, 0xFF);
        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var s = new StackPanel(); s.Children.Add(Caption("", L.T("Буфер обмена"), accent)); s.Children.Add(list);
        body.Children.Add(s);
        string last = "-";
        return () =>
        {
            var items = _clips.Where(c => !string.IsNullOrWhiteSpace(c.Text)).Take(8).ToList();
            var key = string.Join("|", items.Select(c => c.Text));
            if (key == last) return;
            last = key;
            list.Children.Clear();
            if (items.Count == 0) list.Children.Add(W(L.T("Скопируйте что-нибудь"), 13, null, Dim));
            foreach (var c in items)
            {
                var text = c.Text!.Replace('\n', ' ').Trim();
                var copy = c.Text;
                var line = new DockPanel();
                var ago = W(Ago(c.At), 11, null, Dim); DockPanel.SetDock(ago, Dock.Right); ago.Margin = new Thickness(8, 0, 0, 0);
                line.Children.Add(ago); line.Children.Add(W(text, 13));
                var row = WKey(line, null, null, () =>
                {
                    try { Clipboard.SetText(copy); ShowToast("\uE8C8", L.T("Скопировано"), text.Length > 40 ? text[..40] + "…" : text); }
                    catch (Exception ex) { Log.Write("widget copy: " + ex.Message); ShowToast("\uE783", L.T("Не получилось скопировать"), L.T("Буфер обмена занят другой программой")); }
                }, double.NaN, 34, 11);
                line.Margin = new Thickness(12, 0, 12, 0); line.HorizontalAlignment = HorizontalAlignment.Stretch;
                row.Margin = new Thickness(0, 0, 0, 6);
                list.Children.Add(row);
            }
        };
    }

    private Action RemindersBig(Grid body)
    {
        var accent = Solid(0x0A, 0x84, 0xFF);
        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var s = new StackPanel(); s.Children.Add(Caption("", L.T("Напоминания"), accent)); s.Children.Add(list);
        body.Children.Add(s);
        string last = "-";
        return () =>
        {
            var now = DateTime.Now;
            var items = _calendar.Upcoming(7).Where(e => e.AllDay || e.Start > now.AddMinutes(-5)).Take(8).ToList();
            var key = string.Join("|", items.Select(e => e.Title + e.Start.Ticks));
            if (key == last) return;
            last = key;
            list.Children.Clear();
            if (items.Count == 0) list.Children.Add(W(L.T("Ничего не запланировано"), 13, null, Dim));
            DateTime? day = null;
            foreach (var e in items)
            {
                if (day != e.Start.Date)
                {
                    day = e.Start.Date;
                    var h = W(day == now.Date ? L.T("Сегодня") : day == now.Date.AddDays(1) ? L.T("Завтра") : e.Start.ToString("dddd, d MMMM", Loc.Culture), 11.5, FontWeights.SemiBold, accent);
                    h.Margin = new Thickness(0, list.Children.Count > 0 ? 6 : 0, 0, 6);
                    list.Children.Add(h);
                }
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var dot = new Ellipse { Width = 16, Height = 16, Stroke = accent, StrokeThickness = 1.6, Margin = new Thickness(0, 0, 10, 0) };
                var when = W(e.AllDay ? L.T("весь день") : e.Start.ToString("HH:mm"), 12, null, Dim);
                DockPanel.SetDock(dot, Dock.Left); DockPanel.SetDock(when, Dock.Right); when.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(dot); row.Children.Add(when); row.Children.Add(W(e.Title, 14));
                list.Children.Add(row);
            }
        };
    }

    // ------------------------------------------------------------------ system, timer, world clock

    private Action SystemBig(Grid body)
    {
        var (stack, part) = Parts();
        var rings = SystemWidget(part(124));
        var info = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(info);
        var top = TopWidget(part(150), 4);
        body.Children.Add(stack);
        TextBlock Line(string label)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            s.Children.Add(W(label, 12, FontWeights.SemiBold, Dim));
            var v = W("", 14, FontWeights.SemiBold); s.Children.Add(v);
            info.Children.Add(s);
            return v;
        }
        var gpu = Line(L.T("Видеокарта")); var ram = Line(L.T("Память")); var net = Line(L.T("Сеть")); var up = Line(L.T("Без перезагрузки"));
        return () =>
        {
            rings(); top();
            var st = _stats.Last;
            gpu.Text = st?.GpuName is { Length: > 0 } g ? g : "—";
            ram.Text = st == null ? "—" : L.F("{0:0.0} из {1:0} ГБ", st.RamUsedGb, st.RamTotalGb);
            net.Text = "↓ " + Bytes(_netDown) + "/s  ↑ " + Bytes(_netUp) + "/s";
            var t = TimeSpan.FromMilliseconds(Environment.TickCount64);
            up.Text = t.TotalDays >= 1 ? L.F("{0} д {1} ч", (int)t.TotalDays, t.Hours) : L.F("{0} ч {1} мин", t.Hours, t.Minutes);
        };
    }

    private Action TimerOpen(Grid body)
    {
        var accent = Color.FromRgb(0xFF, 0x9F, 0x0A);
        var (ring, set) = RingGauge(150, 10, accent);
        ring.HorizontalAlignment = HorizontalAlignment.Center;
        var display = W("", 30, FontWeights.Light, null, big: true); display.HorizontalAlignment = HorizontalAlignment.Center; display.VerticalAlignment = VerticalAlignment.Center;
        ring.Children.Add(display);
        var presets = new UniformGrid { Columns = 4, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var m in new[] { 1, 3, 5, 10, 15, 25, 45, 60 })
        {
            var p = Pill(L.F("{0} мин", m), () => StartQuickTimer(TimeSpan.FromMinutes(m)), null, double.NaN, 32);
            p.Margin = new Thickness(3);
            presets.Children.Add(p);
        }
        var pauseIcon = Glyph("", 14);
        var pause = WKey(pauseIcon, new SolidColorBrush(Color.FromArgb(0x50, accent.R, accent.G, accent.B)), null, () => { if (_timer.Running) _timer.Pause(); else if (_timer.Active) _timer.Start(); }, 46, 46, name: L.T("Пауза"));
        var reset = WKey(Glyph("\uE72C", 14), null, null, () => _timer.Reset(), 46, 46, name: L.T("Сбросить"));
        reset.Margin = new Thickness(0, 0, 12, 0);
        var buttons = Row(reset, pause); buttons.HorizontalAlignment = HorizontalAlignment.Center; buttons.Margin = new Thickness(0, 10, 0, 0);
        var s = new StackPanel(); s.Children.Add(Caption("", L.T("Таймер"), new SolidColorBrush(accent))); s.Children.Add(ring); s.Children.Add(buttons); s.Children.Add(presets);
        ring.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(s);
        return () =>
        {
            display.Text = _timer.Active ? _timer.Display : "0:00";
            set(_timer.Active ? Math.Max(0.02, _timer.Progress) : 0);
            pauseIcon.Text = _timer.Running ? "" : "";
            buttons.Visibility = _timer.Active ? Visibility.Visible : Visibility.Collapsed;
        };
    }

    /// <summary>Cities the world clock can show (names are translated where shown).</summary>
    private static readonly (string Name, string Zone)[] AllCities =
    [
        ("Нью-Йорк", "Eastern Standard Time"), ("Лос-Анджелес", "Pacific Standard Time"), ("Лондон", "GMT Standard Time"), ("Париж", "Romance Standard Time"),
        ("Берлин", "W. Europe Standard Time"), ("Москва", "Russian Standard Time"), ("Киев", "FLE Standard Time"), ("Стамбул", "Turkey Standard Time"),
        ("Дубай", "Arabian Standard Time"), ("Дели", "India Standard Time"), ("Пекин", "China Standard Time"), ("Сингапур", "Singapore Standard Time"),
        ("Токио", "Tokyo Standard Time"), ("Сидней", "AUS Eastern Standard Time"),
    ];

    private IEnumerable<(string Name, string Zone)> ChosenCities()
    {
        var chosen = _settings.WorldCities.Select(n => AllCities.FirstOrDefault(c => c.Name == n)).Where(c => c.Name != null).ToList();
        return chosen.Count > 0 ? chosen : AllCities.Take(3);
    }

    private Action WorldClockBig(Grid body)
    {
        var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var s = new StackPanel(); s.Children.Add(Caption("", L.T("Мировое время"), Solid(0xFF, 0x9F, 0x0A))); s.Children.Add(list);
        body.Children.Add(s);
        var rows = new List<(TextBlock Time, TextBlock Diff, TextBlock Dn, TimeZoneInfo? Zone)>();
        foreach (var (name, zone) in ChosenCities().Take(6))
        {
            TimeZoneInfo? tz = null;
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(zone); } catch { /* unknown time zone: local time */ }
            var time = W("", 30, FontWeights.Light, null, big: true); DockPanel.SetDock(time, Dock.Right);
            var dn = Glyph("", 13, Dim); dn.Margin = new Thickness(0, 0, 10, 0); DockPanel.SetDock(dn, Dock.Left);
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var diff = W("", 11.5, null, Dim);
            left.Children.Add(W(L.T(name), 15, FontWeights.SemiBold)); left.Children.Add(diff);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(time); row.Children.Add(dn); row.Children.Add(left);
            list.Children.Add(row);
            rows.Add((time, diff, dn, tz));
        }
        return () =>
        {
            var now = DateTime.Now;
            foreach (var (time, diff, dn, tz) in rows)
            {
                if (tz == null) { time.Text = "—"; continue; }
                var t = TimeZoneInfo.ConvertTime(now, tz);
                time.Text = t.ToString("HH:mm");
                int h = (int)Math.Round((t - now).TotalHours);
                diff.Text = (t.Date > now.Date ? L.T("Завтра") : t.Date < now.Date ? L.T("Вчера") : L.T("Сегодня")) + (h == 0 ? "" : $", {(h > 0 ? "+" : "")}{h} {L.T("ч")}");
                dn.Text = t.Hour is >= 6 and < 20 ? "" : "";
            }
        };
    }
}

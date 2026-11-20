using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// The clock widget. Small: a watch face in one of three styles (classic, minimal, digital) with a sweeping
/// seconds hand. Large: tabs — the face with a ring for how much of the day has gone, a timer, a pomodoro and
/// alarms. When a timer, a pomodoro phase or an alarm ends, the island says so with a soft chime.
/// </summary>
public partial class MainWindow
{
    private static readonly Color ClockOrange = Color.FromRgb(0xFF, 0x9F, 0x0A);

    // ------------------------------------------------------------------ faces

    /// <summary>A face in the chosen style, 134 px; the action refreshes it (once a second is enough: the hand sweeps by itself).</summary>
    private FrameworkElement ClockFace(string style, out Action update)
    {
        if (style == "digital") return DigitalFace(out update);
        bool minimal = style == "minimal";
        const double S = 134, C = S / 2;
        var ink = minimal ? Brushes.White : Solid(0x1C, 0x1C, 0x1E);
        var face = new Canvas { Width = S, Height = S };
        face.Children.Add(new Ellipse
        {
            Width = S, Height = S,
            Fill = minimal ? Solid(0xFF, 0xFF, 0xFF, 0x14) : Solid(0xFF, 0xFF, 0xFF, 0xF2),
            Stroke = minimal ? Solid(0xFF, 0xFF, 0xFF, 0x40) : null, StrokeThickness = 1,
        });
        // thin marks: 60 for the minutes (classic), heavier ones for the hours
        for (int i = 0; i < 60; i++)
        {
            bool isHour = i % 5 == 0;
            if (minimal && !isHour) continue;
            bool quarter = i % 15 == 0;
            double a = i * Math.PI / 30, r1 = quarter ? 53 : isHour ? 56 : 60, r2 = 63;
            face.Children.Add(new Line
            {
                X1 = C + Math.Sin(a) * r1, Y1 = C - Math.Cos(a) * r1, X2 = C + Math.Sin(a) * r2, Y2 = C - Math.Cos(a) * r2,
                Stroke = ink, Opacity = isHour ? 1 : 0.35, StrokeThickness = quarter ? 2.4 : isHour ? 1.6 : 0.8,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
        }
        if (!minimal)
            foreach (var (n, i) in new[] { ("12", 0), ("3", 3), ("6", 6), ("9", 9) })
            {
                double a = i * Math.PI / 6;
                var t = new TextBlock { Text = n, FontFamily = Display, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = ink };
                t.Measure(new Size(100, 100));
                Canvas.SetLeft(t, C + Math.Sin(a) * 40 - t.DesiredSize.Width / 2); Canvas.SetTop(t, C - Math.Cos(a) * 40 - t.DesiredSize.Height / 2);
                face.Children.Add(t);
            }
        Line Hand(double len, double back, double thick, Brush b) => new()
        {
            X1 = C, Y1 = C + back, X2 = C, Y2 = C - len, Stroke = b, StrokeThickness = thick,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        var orange = new SolidColorBrush(ClockOrange);
        var hour = Hand(32, 0, 5, ink); var minute = Hand(48, 0, 3.4, ink); var second = Hand(56, 12, 1.3, orange);
        var hr = new RotateTransform(0, C, C); var mr = new RotateTransform(0, C, C); var sr = new RotateTransform(0, C, C);
        hour.RenderTransform = hr; minute.RenderTransform = mr; second.RenderTransform = sr;
        face.Children.Add(hour); face.Children.Add(minute); face.Children.Add(second);
        var cap = new Ellipse { Width = 6, Height = 6, Fill = orange }; Canvas.SetLeft(cap, C - 3); Canvas.SetTop(cap, C - 3);
        face.Children.Add(cap);
        // the seconds hand sweeps: one smooth turn a minute, drawn by WPF at 12 frames a second (no code per frame)
        void Sweep()
        {
            var now = DateTime.Now;
            double from = (now.Second + now.Millisecond / 1000.0) * 6;
            var turn = new DoubleAnimation(from, from + 360, TimeSpan.FromSeconds(60)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(turn, 12);   // 6° a second: 12 frames (0.5° each) look as smooth as 30 and cost far less
            sr.BeginAnimation(RotateTransform.AngleProperty, turn);
        }
        bool smooth = !Motion.Reduced && _settings.ClockSweep;
        if (smooth) Sweep();
        face.Loaded += (_, _) =>
        {
            if (Window.GetWindow(face) is not Views.DesktopWidgetWindow host) return;
            host.PausedChanged += paused =>
            {
                if (!smooth) return;
                if (paused) { double a = sr.Angle; sr.BeginAnimation(RotateTransform.AngleProperty, null); sr.Angle = a; }
                else Sweep();
            };
        };
        update = () =>
        {
            var now = DateTime.Now;
            hr.Angle = (now.Hour % 12 + now.Minute / 60.0) * 30;
            mr.Angle = (now.Minute + now.Second / 60.0) * 6;
            double real = (now.Second + now.Millisecond / 1000.0) * 6;
            if (!smooth) { sr.BeginAnimation(RotateTransform.AngleProperty, null); sr.Angle = now.Second * 6; }
            // after sleep or a clock change the sweep is set right again
            else if (Math.Abs(((sr.Angle - real) % 360 + 540) % 360 - 180) > 3) Sweep();
        };
        update();
        return face;
    }

    private FrameworkElement DigitalFace(out Action update)
    {
        var time = W("", 46, FontWeights.Light, null, big: true); time.HorizontalAlignment = HorizontalAlignment.Center;
        var date = W("", 12.5, FontWeights.SemiBold, Dim); date.HorizontalAlignment = HorizontalAlignment.Center;
        var (track, fill) = Bar(3, new SolidColorBrush(ClockOrange));
        track.Margin = new Thickness(18, 8, 18, 0);
        var s = new StackPanel { Width = 134, VerticalAlignment = VerticalAlignment.Center, Children = { time, date, track } };
        update = () =>
        {
            var now = DateTime.Now;
            time.Text = now.ToString("HH:mm");
            date.Text = now.ToString("ddd, d MMM", Loc.Culture);
            fill.Width = Math.Max(0, track.ActualWidth * now.Second / 59.0);
        };
        update();
        return s;
    }

    // ------------------------------------------------------------------ the widget

    private Action ClockWidget(Grid body)
    {
        if (_buildSize != WidgetSize.Large)
        {
            body.Margin = new Thickness(12);
            var face = ClockFace(_settings.ClockFace, out var tick);
            body.Children.Add(face);
            return tick;
        }

        // large: tabs
        LightText(body);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
        var host = new Grid();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(tabs);
        Grid.SetRow(host, 1); root.Children.Add(host);
        body.Children.Add(root);
        Action? tabTick = null;
        var names = new[] { ("clock", L.T("Часы")), ("timer", L.T("Таймер")), ("pomodoro", L.T("Помодоро")), ("alarm", L.T("Будильник")) };
        void Show(string id)
        {
            _settings.ClockTab = id;
            tabs.Children.Clear();
            foreach (var (key, label) in names)
            {
                bool on = key == id;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(2, 0, 2, 0), Cursor = Cursors.Hand,
                    Background = on ? Solid(0xFF, 0xFF, 0xFF, 0xE6) : Solid(0xFF, 0xFF, 0xFF, 0x1E),
                    Child = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = on ? Solid(0x1C, 0x1C, 0x1E) : Brushes.White },
                };
                System.Windows.Automation.AutomationProperties.SetName(chip, label);
                var k = key;
                chip.MouseLeftButtonUp += (_, e) => { if (_widgetsEditing) return; e.Handled = true; Show(k); };
                tabs.Children.Add(chip);
            }
            host.Children.Clear();
            tabTick = id switch
            {
                "timer" => TimerTab(host),
                "pomodoro" => PomodoroTab(host),
                "alarm" => AlarmTab(host),
                _ => FaceTab(host),
            };
        }
        Show(_settings.ClockTab is "timer" or "pomodoro" or "alarm" ? _settings.ClockTab : "clock");
        return () => tabTick?.Invoke();
    }

    /// <summary>The face, and a ring for how much of the day has gone.</summary>
    private Action FaceTab(Grid host)
    {
        var face = ClockFace(_settings.ClockFace == "digital" ? "classic" : _settings.ClockFace, out var tick);
        var box = new Viewbox { Child = face, Height = 168, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        var (ring, set) = RingGauge(46, 5, AccC(ClockOrange));
        var pct = W("", 11, FontWeights.SemiBold); pct.HorizontalAlignment = HorizontalAlignment.Center; pct.VerticalAlignment = VerticalAlignment.Center;
        ring.Children.Add(pct);
        var line1 = W("", 13, FontWeights.SemiBold); var line2 = W("", 11.5, null, Dim);
        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { line1, line2 } };
        var day = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), Children = { ring, text } };
        host.Children.Add(new StackPanel { Children = { box, day } });
        return () =>
        {
            tick();
            var now = DateTime.Now;
            double f = now.TimeOfDay.TotalHours / 24;
            set(f); pct.Text = $"{f * 100:0}%";
            line1.Text = now.ToString("dddd, d MMMM", Loc.Culture);
            var left = now.Date.AddDays(1) - now;
            line2.Text = L.F("до полуночи {0} ч {1} мин", (int)left.TotalHours, left.Minutes);
        };
    }

    /// <summary>A countdown with a ring: presets, start or pause, reset. The same timer the island shows.</summary>
    private Action TimerTab(Grid host)
    {
        var (ring, set) = RingGauge(150, 8, AccC(ClockOrange));
        var time = W("", 32, FontWeights.Light, null, big: true); time.HorizontalAlignment = HorizontalAlignment.Center;
        var phase = W("", 11.5, FontWeights.SemiBold, Dim); phase.HorizontalAlignment = HorizontalAlignment.Center;
        ring.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { time, phase } });
        ring.HorizontalAlignment = HorizontalAlignment.Center;
        var presets = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var m in new[] { 5, 10, 25, 60 })
        {
            var p = Pill(m < 60 ? L.F("{0} мин", m) : L.T("1 ч"), () => StartQuickTimer(TimeSpan.FromMinutes(m)), null, double.NaN, 28);
            p.Margin = new Thickness(2, 0, 2, 0); p.Padding = new Thickness(8, 0, 8, 0);
            presets.Children.Add(p);
        }
        var (controls, playIcon) = PlayResetRow(() =>
        {
            if (_timer.Kind != TimerKind.Timer) { _timer.SetKind(TimerKind.Timer); _timer.SetDuration(TimeSpan.FromMinutes(5)); }
            if (_timer.Running) _timer.Pause(); else _timer.Start();
            RenderTimer();
        }, () => { if (_timer.Kind == TimerKind.Timer) { _timer.Reset(); RenderTimer(); } });
        host.Children.Add(new StackPanel { Children = { ring, presets, controls } });
        return () =>
        {
            bool mine = _timer.Kind == TimerKind.Timer;
            time.Text = mine ? _timer.Display : "05:00";
            phase.Text = mine && _timer.Active ? _timer.Phase : L.T("таймер");
            set(mine && _timer.Active ? Math.Max(0.01, 1 - _timer.Progress) : 0);
            playIcon.Text = mine && _timer.Running ? "" : "";
        };
    }

    /// <summary>Work and breaks (lengths adjustable), with today's finished sessions.</summary>
    private Action PomodoroTab(Grid host)
    {
        var (ring, set) = RingGauge(132, 8, Color.FromRgb(0xFF, 0x45, 0x3A));
        var time = W("", 30, FontWeights.Light, null, big: true); time.HorizontalAlignment = HorizontalAlignment.Center;
        var phase = W("", 11.5, FontWeights.SemiBold, Dim); phase.HorizontalAlignment = HorizontalAlignment.Center;
        ring.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { time, phase } });
        ring.HorizontalAlignment = HorizontalAlignment.Center;
        var (controls, playIcon) = PlayResetRow(() =>
        {
            if (_timer.Kind != TimerKind.Pomodoro) _timer.SetKind(TimerKind.Pomodoro);
            if (_timer.Running) _timer.Pause(); else _timer.Start();
            RenderTimer();
        }, () => { if (_timer.Kind == TimerKind.Pomodoro) { _timer.Reset(); RenderTimer(); } });
        controls.Margin = new Thickness(0, 8, 0, 0);

        FrameworkElement Stepper(string label, Func<int> get, Action<int> put, int min, int max)
        {
            var value = W("", 13, FontWeights.SemiBold); value.MinWidth = 44; value.TextAlignment = TextAlignment.Center; value.VerticalAlignment = VerticalAlignment.Center;
            void Show() => value.Text = L.F("{0} мин", get());
            Button Step(string glyph, int d) { var b = Pill(glyph, () => { put(Math.Clamp(get() + d, min, max)); ApplyPomodoroSettings(); Show(); }, null, 26, 24); return b; }
            Show();
            var caption = W(label, 11, FontWeights.SemiBold, Dim); caption.HorizontalAlignment = HorizontalAlignment.Center;
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { Step("−", -5), value, Step("+", 5) } };
            return new StackPanel { Margin = new Thickness(6, 0, 6, 0), Children = { caption, row } };
        }
        var lengths = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), Children =
        {
            Stepper(L.T("Работа"), () => _settings.PomodoroWorkMin, v => _settings.PomodoroWorkMin = v, 5, 90),
            Stepper(L.T("Перерыв"), () => _settings.PomodoroBreakMin, v => _settings.PomodoroBreakMin = v, 5, 30),
        } };
        var today = W("", 12, FontWeights.SemiBold, Dim); today.HorizontalAlignment = HorizontalAlignment.Center; today.Margin = new Thickness(0, 8, 0, 0);
        host.Children.Add(new StackPanel { Children = { ring, controls, lengths, today } });
        return () =>
        {
            bool mine = _timer.Kind == TimerKind.Pomodoro;
            time.Text = mine ? _timer.Display : $"{_settings.PomodoroWorkMin:00}:00";
            phase.Text = mine && _timer.Active ? _timer.Phase : L.T("помодоро");
            set(mine && _timer.Active ? Math.Max(0.01, 1 - _timer.Progress) : 0);
            playIcon.Text = mine && _timer.Running ? "" : "";
            int n = PomodoroCount.Today(_settings.PomodoroCountedOn, _settings.PomodoroDone, DateTime.Now);
            today.Text = n == 0 ? L.T("Сегодня ещё ни одной сессии") : L.F("Сегодня сессий: {0}", n) + "  " + new string('●', Math.Min(n, 8));
        };
    }

    /// <summary>Play/pause and reset, side by side.</summary>
    private (StackPanel Row, TextBlock PlayIcon) PlayResetRow(Action play, Action reset)
    {
        var icon = Glyph("", 15);
        var playKey = WKey(icon, Solid(0xFF, 0xFF, 0xFF, 0x30), null, play, 52, 36, 18, name: L.T("Старт или пауза"));
        var resetKey = WKey(Glyph("", 13), Solid(0xFF, 0xFF, 0xFF, 0x1A), null, reset, 40, 36, 18, name: L.T("Сбросить"));
        resetKey.Margin = new Thickness(8, 0, 0, 0);
        return (new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), Children = { playKey, resetKey } }, icon);
    }

    /// <summary>Alarms: the soonest ring, the list (on/off, delete), and the one picked for editing.</summary>
    private Action AlarmTab(Grid host)
    {
        var next = W("", 13, FontWeights.SemiBold); next.HorizontalAlignment = HorizontalAlignment.Center; next.TextAlignment = TextAlignment.Center;
        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var editor = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        host.Children.Add(new StackPanel { Children = { next, list, editor } });
        Alarm? picked = _settings.Alarms.FirstOrDefault();

        void Save() { if (!Settings.ReadOnly) _settings.Save(); }
        void Rebuild()
        {
            list.Children.Clear();
            foreach (var a in _settings.Alarms.Take(4))
            {
                var alarm = a;
                var time = W(a.Time.ToString(@"hh\:mm"), 22, FontWeights.Light, a.Enabled ? Brushes.White : Dim, big: true);
                var days = W(AlarmService.DaysText(a.Days, Loc.Culture), 11, null, Dim); days.VerticalAlignment = VerticalAlignment.Center; days.Margin = new Thickness(10, 4, 0, 0);
                var onOff = new Border
                {
                    Width = 38, Height = 22, CornerRadius = new CornerRadius(11), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                    Background = a.Enabled ? new SolidColorBrush(AccC(Color.FromRgb(0x30, 0xD1, 0x58))) : Solid(0xFF, 0xFF, 0xFF, 0x26),
                    Child = new Ellipse { Width = 18, Height = 18, Fill = Brushes.White, HorizontalAlignment = a.Enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(2, 0, 2, 0) },
                };
                System.Windows.Automation.AutomationProperties.SetName(onOff, L.T("Включить или выключить"));
                onOff.MouseLeftButtonUp += (_, e) => { if (_widgetsEditing) return; e.Handled = true; alarm.Enabled = !alarm.Enabled; Save(); Rebuild(); };
                var del = WKey(Glyph("", 10, Dim), Brushes.Transparent, null, () => { _settings.Alarms.Remove(alarm); if (picked == alarm) picked = _settings.Alarms.FirstOrDefault(); Save(); Rebuild(); }, 26, 26, 13, name: L.T("Удалить"));
                del.VerticalAlignment = VerticalAlignment.Center;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                DockPanel.SetDock(del, Dock.Right); DockPanel.SetDock(onOff, Dock.Right);
                row.Children.Add(del); row.Children.Add(onOff);
                row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { time, days } });
                row.MouseLeftButtonUp += (_, e) => { if (_widgetsEditing) return; e.Handled = true; picked = alarm; Rebuild(); };
                var plate = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 4, 2), Margin = new Thickness(0, 0, 0, 4), Child = row,
                                         Background = a == picked ? Solid(0xFF, 0xFF, 0xFF, 0x22) : Solid(0xFF, 0xFF, 0xFF, 0x0E) };
                list.Children.Add(plate);
            }
            if (_settings.Alarms.Count < 4)
            {
                var add = Pill("+ " + L.T("Будильник"), () =>
                {
                    var a = new Alarm { Time = new TimeSpan(7, 0, 0), Days = AlarmService.Weekdays };
                    _settings.Alarms.Add(a); picked = a; Save(); Rebuild();
                }, null, double.NaN, 28);
                add.HorizontalAlignment = HorizontalAlignment.Center; add.Padding = new Thickness(12, 0, 12, 0); add.Margin = new Thickness(0, 2, 0, 0);
                list.Children.Add(add);
            }

            // the picked alarm: hours and minutes, and which days
            editor.Children.Clear();
            if (picked == null || !_settings.Alarms.Contains(picked)) return;
            var p = picked;
            Button Step(string text, TimeSpan d) => Pill(text, () => { p.Time = TimeSpan.FromMinutes(((p.Time + d).TotalMinutes % 1440 + 1440) % 1440); p.Enabled = true; p.LastRang = default; Save(); Rebuild(); }, null, 34, 24);
            var steps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children =
            {
                Step("−1ч", TimeSpan.FromHours(-1)), Step("+1ч", TimeSpan.FromHours(1)),
                new Border { Width = 10 },
                Step("−5", TimeSpan.FromMinutes(-5)), Step("+5", TimeSpan.FromMinutes(5)),
            } };
            foreach (var b in steps.Children.OfType<Button>()) b.Margin = new Thickness(2, 0, 2, 0);
            var dayChips = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var (label, mask) in new[] { (L.T("Будни"), AlarmService.Weekdays), (L.T("Выходные"), AlarmService.Weekend), (L.T("Каждый день"), AlarmService.Everyday), (L.T("Однократно"), 0) })
            {
                bool on = p.Days == mask;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(2), Cursor = Cursors.Hand,
                    Background = on ? Solid(0xFF, 0xFF, 0xFF, 0xE6) : Solid(0xFF, 0xFF, 0xFF, 0x1E),
                    Child = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = on ? Solid(0x1C, 0x1C, 0x1E) : Brushes.White },
                };
                var m = mask;
                chip.MouseLeftButtonUp += (_, e) => { if (_widgetsEditing) return; e.Handled = true; p.Days = m; p.Enabled = true; Save(); Rebuild(); };
                dayChips.Children.Add(chip);
            }
            editor.Children.Add(steps); editor.Children.Add(dayChips);
        }
        Rebuild();
        int shownCount = -1;
        return () =>
        {
            if (_settings.Alarms.Count != shownCount) { shownCount = _settings.Alarms.Count; Rebuild(); }
            var now = DateTime.Now;
            var soon = AlarmService.Soonest(_settings.Alarms, now);
            next.Text = soon is { } t ? L.F("Будильник {0}", AlarmService.Until(t - now)) : _settings.Alarms.Count == 0 ? L.T("Будильников пока нет") : L.T("Все будильники выключены");
        };
    }

    // ------------------------------------------------------------------ alarms ringing, pomodoro settings

    /// <summary>Once a second (the island's slow tick): an alarm that is due rings in the island with a soft chime.</summary>
    private void CheckAlarms()
    {
        if (Settings.ReadOnly || _settings.Alarms.Count == 0) return;
        var now = DateTime.Now;
        foreach (var a in _settings.Alarms)
        {
            if (!AlarmService.IsDue(a, now)) continue;
            AlarmService.Rang(a, now);
            _settings.Save();
            PlayChime();
            ShowToast("", L.T("Будильник"), a.Label.Length > 0 ? a.Label : a.Time.ToString(@"hh\:mm"), null, new SolidColorBrush(ClockOrange), ms: 15000, keep: true);
            Log.Write($"alarm rang: {a.Time:hh\\:mm}");
        }
    }

    private void ApplyPomodoroSettings()
    {
        TimerService.Work = TimeSpan.FromMinutes(Math.Clamp(_settings.PomodoroWorkMin, 5, 90));
        TimerService.ShortBreak = TimeSpan.FromMinutes(Math.Clamp(_settings.PomodoroBreakMin, 5, 30));
        TimerService.LongBreak = TimeSpan.FromMinutes(Math.Clamp(_settings.PomodoroLongMin, 10, 45));
        if (_timer.Kind == TimerKind.Pomodoro && !_timer.Active) _timer.SetKind(TimerKind.Pomodoro);   // a stopped pomodoro takes the new length
        if (!Settings.ReadOnly) _settings.Save();
    }
}

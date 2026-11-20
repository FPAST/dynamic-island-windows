using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DynamicIsland.Services;
using DynamicIsland.Views;

namespace DynamicIsland;

/// <summary>
/// The weather widget. Its main line is advice for the next hours ("Дождь начнётся около 23:40") rather than the
/// bare temperature the island already shows. S: the advice; M: plus a 12-hour chart; L: plus 24 hours and the
/// week with iOS-style range bars. Without network the last weather stays, marked with its time.
/// </summary>
public partial class MainWindow
{
    /// <summary>"данные на 14:05" when the weather shown is more than half an hour old (no network).</summary>
    private static string StaleNote(WeatherData w)
    {
        if ((DateTime.Now - w.FetchedAt).TotalMinutes <= 30) return "";
        var at = w.FetchedAt.Date == DateTime.Today ? w.FetchedAt.ToString("H:mm") : w.FetchedAt.ToString("d MMM, H:mm", Loc.Culture);
        return L.F("данные на {0}", at);
    }

    /// <summary>Made-up weather for screenshots (never the user's city): rain from 3 hours on, a varied week.</summary>
    private static WeatherData DemoWeather(DateTime now, DateTime fetched)
    {
        var day0 = now.Date;
        var hours = Enumerable.Range(0, 24 * 7).Select(i => day0.AddHours(i)).Select(t =>
        {
            double h = (t - now).TotalHours;
            bool rain = h is >= 3 and < 6;
            return new WeatherHour(t, Math.Round(9 + 4 * Math.Sin((t.Hour - 9) / 24.0 * 2 * Math.PI) + (t.Date - day0).TotalDays * 0.4, 1),
                                   rain ? 0.8 : 0, rain ? 85 : h is >= 2 and < 7 ? 40 : 8, h is >= 8 and < 12 ? 9 : 4, t.Hour is > 9 and < 15 ? 2 : 0,
                                   rain ? 61 : 2, t.Hour is > 7 and < 17);
        }).ToList();
        var start = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute / 15 * 15, 0).AddMinutes(15);
        var quarters = Enumerable.Range(0, 24).Select(i => start.AddMinutes(15 * i))
                                 .Select(t => new WeatherQuarter(t, (t - now).TotalHours is >= 3.25 and < 6 ? 0.3 : 0)).ToList();
        int[] codes = [2, 61, 3, 0, 1, 63, 71];
        double[] lows = [6, 7, 5, 3, 4, 2, -2], highs = [13, 12, 10, 11, 14, 8, 3];
        var days = Enumerable.Range(0, 7).Select(i => new WeatherDay(day0.AddDays(i), lows[i], highs[i], codes[i], i is 1 or 5 ? 80 : i == 6 ? 60 : 10)).ToList();
        return new WeatherData("Одесса", 11, 9, 2, true, 78, 755, 4, 210, day0.AddHours(7.03), day0.AddHours(16.35), 13, 6, 2, 0, 0, 40,
                               hours.Where(x => x.Time > now).Take(6).Select(x => new HourForecast(x.Time, x.Temp, x.Code, x.IsDay)).ToList())
        { Hourly = hours, Minutely = quarters, Days = days, Aqi = 21, Pm25 = 5.3, FetchedAt = fetched, UtcOffset = TimeZoneInfo.Local.GetUtcOffset(now) };
    }

    /// <summary>European AQI in words (its own bands).</summary>
    private static string AqiWord(double aqi) => aqi switch
    {
        <= 20 => L.T("хороший"),
        <= 40 => L.T("приемлемый"),
        <= 60 => L.T("средний"),
        <= 80 => L.T("плохой"),
        <= 100 => L.T("очень плохой"),
        _ => L.T("опасный"),
    };

    private Action WeatherWidget(Grid body, Border wash)
    {
        var size = _buildSize;
        LightText(body);
        var place = W("", size == WidgetSize.Small ? 12.5 : 14, FontWeights.SemiBold);
        place.TextTrimming = TextTrimming.CharacterEllipsis;
        var stale = W("", 10.5, FontWeights.SemiBold, Dim);
        var temp = W("—", size == WidgetSize.Small ? 40 : 44, FontWeights.Light, null, big: true); temp.Margin = new Thickness(-2, -4, 0, 0);
        var icon = Glyph("", size == WidgetSize.Small ? 18 : 22, null, symbol: true);
        var desc = W("", 12, FontWeights.SemiBold, Dim);
        var hl = W("", 12, FontWeights.SemiBold, Dim);
        var advice = W("", size == WidgetSize.Small ? 11.5 : 13, FontWeights.SemiBold);
        advice.TextWrapping = TextWrapping.Wrap; advice.TextTrimming = TextTrimming.CharacterEllipsis; advice.MaxHeight = size == WidgetSize.Small ? 32 : 36;
        WeatherChart? chart = null;
        StackPanel? days = null;
        // the slow background: under the text, over the sky; stops whenever nobody sees it
        var atmosphere = new WeatherAtmosphere();
        wash.Child = atmosphere;
        bool seen = true;
        void Breathe() => atmosphere.Running = _settings.WeatherAtmosphere && !Motion.Reduced && seen;
        wash.Loaded += (_, _) =>
        {
            if (Window.GetWindow(wash) is not Views.DesktopWidgetWindow host) return;
            host.PausedChanged += paused => { seen = !paused; Breathe(); };
        };

        switch (size)
        {
            case WidgetSize.Small:
            {
                icon.HorizontalAlignment = HorizontalAlignment.Right; icon.VerticalAlignment = VerticalAlignment.Top;
                var top = new StackPanel { Margin = new Thickness(0, 0, 24, 0), Children = { place, temp } };
                var bottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { advice, stale } };
                body.Children.Add(top); body.Children.Add(icon); body.Children.Add(bottom);
                break;
            }
            case WidgetSize.Medium:
            {
                // the temperature on the left; the advice and the next 12 hours on the right
                var left = new StackPanel { Width = 100, Children = { place, temp, desc, stale } };
                desc.TextTrimming = TextTrimming.CharacterEllipsis;
                chart = new WeatherChart { Span = 12, Margin = new Thickness(0, 6, 0, 0) };
                var right = new Grid { Margin = new Thickness(12, 0, 0, 0) };
                right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                right.RowDefinitions.Add(new RowDefinition());
                right.Children.Add(advice);
                Grid.SetRow(chart, 1); right.Children.Add(chart);
                var dock = new DockPanel(); DockPanel.SetDock(left, Dock.Left); dock.Children.Add(left); dock.Children.Add(right);
                body.Children.Add(dock);
                break;
            }
            default:
            {
                var head = new DockPanel();
                // "данные на 7:35" goes under the range on the right: the week below keeps its room
                var side = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { icon, desc, hl, stale } };
                foreach (FrameworkElement e in side.Children) e.HorizontalAlignment = HorizontalAlignment.Right;
                DockPanel.SetDock(side, Dock.Right);
                head.Children.Add(side); head.Children.Add(new StackPanel { Children = { place, temp } });
                advice.Margin = new Thickness(0, 4, 0, 0);
                chart = new WeatherChart { Span = 24, Height = 74, Margin = new Thickness(0, 6, 0, 2) };
                days = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                var g = new Grid();
                foreach (var (el, star) in new (FrameworkElement, bool)[] { (head, false), (advice, false), (chart, false), (days, true) })
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = star ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
                    Grid.SetRow(el, g.RowDefinitions.Count - 1); g.Children.Add(el);
                }
                body.Children.Add(g);
                break;
            }
        }

        object? shown = "none";
        int shownMinute = -1;
        return () =>
        {
            var w = _lastWeather;
            // redrawn when the weather changes and every 5 minutes (the advice and "now" move with time)
            int minute = DateTime.Now.Hour * 12 + DateTime.Now.Minute / 5;
            if (ReferenceEquals(w, shown) && minute == shownMinute) return;
            shown = w; shownMinute = minute;
            wash.Background = Sky(w);
            atmosphere.Show(w == null ? WeatherAtmosphere.Kind.None : WeatherAtmosphere.For(w.Code, w.IsDay));
            Breathe();
            if (w == null)
            {
                place.Text = _settings.City.Length > 0 ? _settings.City : L.T("Погода");
                advice.Text = L.T("Обновляю…");
                return;
            }
            var now = w.PlaceNow;
            place.Text = w.Place;
            stale.Text = StaleNote(w);
            stale.Visibility = stale.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            temp.Text = $"{Math.Round(w.Temp)}°";
            icon.Text = WeatherService.Glyph(w.Code, w.IsDay);
            desc.Text = WeatherService.Describe(w.Code);
            hl.Text = L.F("Макс.: {0}°, мин.: {1}°", Math.Round(w.Max), Math.Round(w.Min));
            advice.Text = WeatherAdvice.Build(w, now);
            chart?.Show(w.Hourly, now);
            if (chart != null) chart.Visibility = w.Hourly.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (days != null) FillWeek(days, w, now);
        };
    }

    /// <summary>The week, iOS style: day, sky, chance of rain, low, a bar of the day's range on the week's scale, high.</summary>
    private void FillWeek(StackPanel days, WeatherData w, DateTime now)
    {
        days.Children.Clear();
        var week = w.Days.Where(d => d.Date >= now.Date).Take(7).ToList();
        if (week.Count == 0) return;
        double lo = week.Min(d => d.Min), hi = week.Max(d => d.Max), span = Math.Max(1, hi - lo);
        foreach (var d in week)
        {
            var row = new Grid { Height = 18 };
            foreach (var width in new[] { 56.0, 22, 34, 28, -1, 28 })
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
            void Put(UIElement e, int col) { Grid.SetColumn(e, col); row.Children.Add(e); }
            var name = W(d.Date == now.Date ? L.T("Сегодня") : d.Date.ToString("ddd", Loc.Culture), 12, FontWeights.SemiBold);
            name.VerticalAlignment = VerticalAlignment.Center; name.TextTrimming = TextTrimming.CharacterEllipsis;
            Put(name, 0);
            var sky = Glyph(WeatherService.Glyph(d.Code, true), 12.5, null, symbol: true); sky.VerticalAlignment = VerticalAlignment.Center; sky.HorizontalAlignment = HorizontalAlignment.Center;
            Put(sky, 1);
            if (d.Chance >= 30)
            {
                var ch = W($"{d.Chance:0}%", 10.5, FontWeights.SemiBold, Solid(0x7C, 0xC4, 0xFF)); ch.VerticalAlignment = VerticalAlignment.Center;
                Put(ch, 2);
            }
            var min = W($"{Math.Round(d.Min)}°", 12, FontWeights.SemiBold, Dim); min.HorizontalAlignment = HorizontalAlignment.Right; min.VerticalAlignment = VerticalAlignment.Center; min.Margin = new Thickness(0, 0, 6, 0);
            Put(min, 3);
            var max = W($"{Math.Round(d.Max)}°", 12, FontWeights.SemiBold); max.HorizontalAlignment = HorizontalAlignment.Right; max.VerticalAlignment = VerticalAlignment.Center;
            Put(max, 5);
            // the bar: the whole week's range faint, this day's part coloured cold→warm; today: a dot for now
            var bar = new Grid { Height = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Solid(0xFF, 0xFF, 0xFF, 0x26) });
            var part = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = new LinearGradientBrush(TempColor(d.Min), TempColor(d.Max), 0) };
            bar.Children.Add(part);
            Ellipse? dot = null;
            if (d.Date == now.Date)
            {
                dot = new Ellipse { Width = 6, Height = 6, Fill = Brushes.White, Stroke = Solid(0, 0, 0, 0x50), StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, -1, 0, -1) };
                bar.Children.Add(dot);
            }
            bar.SizeChanged += (_, _) =>
            {
                double bw = bar.ActualWidth;
                part.Margin = new Thickness(bw * (d.Min - lo) / span, 0, 0, 0);
                part.Width = Math.Max(4, bw * (d.Max - d.Min) / span);
                if (dot != null) dot.Margin = new Thickness(Math.Clamp(bw * (w.Temp - lo) / span - 3, 0, bw - 6), -1, 0, -1);
            };
            Put(bar, 4);
            days.Children.Add(row);
        }
    }

    /// <summary>A temperature's colour: blue when cold, green-yellow when mild, orange-red when hot.</summary>
    private static Color TempColor(double t)
    {
        (double T, Color C)[] stops =
        [
            (-15, Color.FromRgb(0x5A, 0x8C, 0xFF)), (0, Color.FromRgb(0x64, 0xC8, 0xFF)), (12, Color.FromRgb(0x9A, 0xE0, 0x6A)),
            (22, Color.FromRgb(0xFF, 0xD2, 0x4A)), (32, Color.FromRgb(0xFF, 0x7A, 0x3A)),
        ];
        if (t <= stops[0].T) return stops[0].C;
        for (int i = 1; i < stops.Length; i++)
            if (t <= stops[i].T)
            {
                double k = (t - stops[i - 1].T) / (stops[i].T - stops[i - 1].T);
                Color a = stops[i - 1].C, b = stops[i].C;
                return Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
            }
        return stops[^1].C;
    }
}

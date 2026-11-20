namespace DynamicIsland.Services;

/// <summary>
/// The weather widget's main line: what will happen soon, instead of a bare temperature —
/// "Дождь начнётся около 23:40", "Сухо до утра", "Ветер усилится до 12 м/с после 3:00".
/// From the 15-minute precipitation for the next hours and the hourly forecast for the next 12.
/// </summary>
public static class WeatherAdvice
{
    /// <summary>mm in a quarter of an hour / in an hour that count as "it rains".</summary>
    public const double WetQuarter = 0.1, WetHour = 0.2;
    public const double LikelyChance = 60, MaybeChance = 40;

    public static string Build(WeatherData w, DateTime now)
    {
        // Open-Meteo's quarter "14:15" is the precipitation of 14:00–14:15: the first quarter after now is the current one
        var quarters = w.Minutely.Where(q => q.Time > now).OrderBy(q => q.Time).ToList();
        var hours = w.Hourly.Where(h => h.Time > now.AddHours(-1) && h.Time <= now.AddHours(12)).OrderBy(h => h.Time).ToList();
        bool snowNow = IsSnow(w.Code) || w.Temp <= 0.5;

        bool wetNow = w.PrecipNow >= WetQuarter || quarters.Count > 0 && quarters[0].Precip >= WetQuarter;
        if (wetNow)
        {
            // when it stops: the first quarter after which two dry quarters follow
            for (int i = 1; i + 1 < quarters.Count; i++)
                if (quarters[i].Precip < WetQuarter && quarters[i + 1].Precip < WetQuarter)
                    return Stops(snowNow, quarters[i].Time.AddMinutes(-15));   // dry from the start of that quarter
            var afterQuarters = quarters.Count > 0 ? quarters[^1].Time : now;
            var dryHour = hours.FirstOrDefault(h => h.Time > afterQuarters && h.Precip < WetHour && h.Chance < 50);
            if (dryHour != null) return Stops(snowNow, dryHour.Time);
            return snowNow ? L.T("Снег надолго, до конца дня") : L.T("Дождь надолго, до конца дня");
        }

        // starting soon: the 15-minute forecast is the more exact one
        var start = quarters.FirstOrDefault(q => q.Precip >= WetQuarter);
        if (start != null)
        {
            var from = start.Time.AddMinutes(-15);   // it starts within that quarter
            if (from - now <= TimeSpan.FromMinutes(15))
                return snowNow ? L.T("Снег начнётся в ближайшие минуты") : L.T("Дождь начнётся в ближайшие минуты");
            return Starts(snowNow, from);
        }
        var likely = hours.FirstOrDefault(h => h.Time > now && (h.Precip >= WetHour || h.Chance >= LikelyChance));
        if (likely != null) return Starts(IsSnow(likely.Code) || likely.Temp <= 0.5, likely.Time);
        var maybe = hours.FirstOrDefault(h => h.Time > now && h.Chance >= MaybeChance);
        if (maybe != null)
            return IsSnow(maybe.Code) || maybe.Temp <= 0.5 ? L.F("Возможен снег около {0}", T(maybe.Time)) : L.F("Возможен дождь около {0}", T(maybe.Time));

        // dry: the wind, if it gets noticeably stronger
        var ahead = hours.Where(h => h.Time > now).ToList();
        if (ahead.Count > 0)
        {
            double max = ahead.Max(h => h.Wind);
            if (max >= 10 && max >= w.WindSpeed + 3)
            {
                var from = ahead.First(h => h.Wind >= max - 1);
                return L.F("Ветер усилится до {0} м/с после {1}", Math.Round(max), T(from.Time));
            }
        }

        if (ahead.Count == 0) return WeatherService.Describe(w.Code);   // no forecast (an old cache): just the sky
        if (now.Hour >= 17 || now.Hour < 5) return L.T("Сухо до утра");
        if (now.Hour < 11) return L.T("Сухо весь день");
        return L.T("Сухо до ночи");
    }

    private static string Starts(bool snow, DateTime t) => snow ? L.F("Снег начнётся около {0}", T(t)) : L.F("Дождь начнётся около {0}", T(t));
    private static string Stops(bool snow, DateTime t) => snow ? L.F("Снег закончится около {0}", T(t)) : L.F("Дождь закончится около {0}", T(t));

    public static bool IsSnow(int code) => code is >= 71 and <= 77 or 85 or 86;

    /// <summary>"3:00", "23:40" — like the spec's examples, no leading zero.</summary>
    private static string T(DateTime t) => t.ToString("H:mm", System.Globalization.CultureInfo.InvariantCulture);
}

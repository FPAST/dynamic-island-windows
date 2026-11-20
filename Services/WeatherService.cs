using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland.Services;

public sealed record Place(double Lat, double Lon, string Name);

public sealed record HourForecast(DateTime Time, double Temp, int Code, bool IsDay);

/// <summary>One hour of the forecast (the place's local time): mm of rain/snow, % chance, m/s.</summary>
public sealed record WeatherHour(DateTime Time, double Temp, double Precip, double Chance, double Wind, double Uv, int Code, bool IsDay);

/// <summary>A quarter of an hour: mm of precipitation in it.</summary>
public sealed record WeatherQuarter(DateTime Time, double Precip);

public sealed record WeatherDay(DateTime Date, double Min, double Max, int Code, double Chance);

public sealed record WeatherData(
    string Place,
    double Temp,
    double FeelsLike,
    int Code,
    bool IsDay,
    double Humidity,
    double PressureMm,
    double WindSpeed,
    double WindFrom,
    DateTime Sunrise,
    DateTime Sunset,
    double Max,
    double Min,
    double Uv,
    double PrecipNow,
    double PrecipToday,
    double PrecipChance,
    IReadOnlyList<HourForecast> Hours)
{
    /// <summary>Hourly forecast from the start of today, about 7 days.</summary>
    public IReadOnlyList<WeatherHour> Hourly { get; init; } = [];
    /// <summary>The next hours in 15-minute steps (Open-Meteo only).</summary>
    public IReadOnlyList<WeatherQuarter> Minutely { get; init; } = [];
    public IReadOnlyList<WeatherDay> Days { get; init; } = [];
    /// <summary>European air quality index and PM2.5, µg/m³ (null when unknown).</summary>
    public double? Aqi { get; init; }
    public double? Pm25 { get; init; }
    /// <summary>When it was fetched (this PC's clock).</summary>
    public DateTime FetchedAt { get; init; } = DateTime.Now;
    /// <summary>The place's offset from UTC: its "now" is UtcNow + this.</summary>
    public TimeSpan UtcOffset { get; init; } = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
    /// <summary>The city typed in the settings this was fetched for.</summary>
    public string City { get; init; } = "";

    public DateTime PlaceNow => DateTime.UtcNow + UtcOffset;
}

/// <summary>
/// Weather from Open-Meteo (free, no API key). Where Open-Meteo can't be reached (some providers in Russia
/// block it) the weather comes from MET Norway instead, and towns Open-Meteo doesn't know are looked up
/// in OpenStreetMap — so it works without a VPN.
/// </summary>
public sealed class WeatherService
{
    private static readonly HttpClient Http = CreateHttp();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        // MET Norway and OpenStreetMap ask every app to say who it is
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Atoll/1.6 (+https://github.com/Fuwni/dynamic-island-windows)");
        return h;
    }

    private string? _placeFor;
    private Place? _place;
    private DateTime _openMeteoDownUntil;

    /// <summary>Test: go straight to the fallbacks (MET Norway, OpenStreetMap).</summary>
    public bool TestFallback { get; set; }

    /// <summary>Which service gave the last weather (for the log).</summary>
    public string Source { get; private set; } = "";

    public async Task<Place?> ResolveAsync(string city)
    {
        if (string.IsNullOrWhiteSpace(city)) return null;
        city = city.Trim();
        if (_placeFor == city && _place != null) return _place;   // the city didn't change: no new lookup
        Place? place = null;
        Exception? error = null;
        try { if (!TestFallback) place = await GeocodeAsync(city); }
        catch (Exception e) { error = e; }
        if (place == null)
        {
            try { place = await NominatimAsync(city); }
            catch (Exception e) { if (error != null) throw; Log.Write("weather: OpenStreetMap: " + e.Message); }
        }
        if (place == null && error != null) throw error;
        _placeFor = city;
        _place = place;
        return place;
    }

    /// <summary>OpenStreetMap search: knows far more towns and villages, in any language.</summary>
    private static async Task<Place?> NominatimAsync(string city)
    {
        var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(city)}&format=jsonv2&limit=1&accept-language={Loc.Lang}";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (doc.RootElement.GetArrayLength() == 0) return null;
        var r = doc.RootElement[0];
        var name = r.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } nm ? nm : city;
        return new Place(double.Parse(r.GetProperty("lat").GetString()!, Inv), double.Parse(r.GetProperty("lon").GetString()!, Inv), name);
    }

    private static async Task<Place?> GeocodeAsync(string city)
    {
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language={Loc.Lang}&format=json";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;
        var r = results[0];
        return new Place(
            r.GetProperty("latitude").GetDouble(),
            r.GetProperty("longitude").GetDouble(),
            r.TryGetProperty("name", out var n) ? n.GetString() ?? city : city);
    }

    public async Task<WeatherData> GetAsync(Place place)
    {
        if (DateTime.UtcNow >= _openMeteoDownUntil && !TestFallback)
        {
            try
            {
                var w = await OpenMeteoAsync(place);
                Source = "Open-Meteo";
                return w;
            }
            catch (Exception e)
            {
                Log.Write("weather: Open-Meteo unavailable (" + e.Message + "), using MET Norway");
                _openMeteoDownUntil = DateTime.UtcNow.AddHours(1);   // don't wait for it every time
            }
        }
        var m = await MetNoAsync(place);
        Source = "MET Norway";
        return m;
    }

    private static async Task<WeatherData> OpenMeteoAsync(Place place)
    {
        var url =
            "https://api.open-meteo.com/v1/forecast?latitude=" + place.Lat.ToString(Inv) +
            "&longitude=" + place.Lon.ToString(Inv) +
            "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,surface_pressure,wind_speed_10m,wind_direction_10m,uv_index,precipitation" +
            "&minutely_15=precipitation&forecast_minutely_15=24" +
            "&hourly=temperature_2m,weather_code,is_day,precipitation,precipitation_probability,wind_speed_10m,uv_index" +
            "&daily=weather_code,sunrise,sunset,temperature_2m_max,temperature_2m_min,precipitation_sum,precipitation_probability_max" +
            "&timezone=auto&forecast_days=7&wind_speed_unit=ms";
        var w = ParseOpenMeteo(await Http.GetStringAsync(url), place.Name);
        try
        {
            // air quality is a separate service: without it the weather is still shown
            var aq = "https://air-quality-api.open-meteo.com/v1/air-quality?latitude=" + place.Lat.ToString(Inv) + "&longitude=" + place.Lon.ToString(Inv) + "&current=european_aqi,pm2_5&timezone=auto";
            var (aqi, pm) = ParseAirQuality(await Http.GetStringAsync(aq));
            w = w with { Aqi = aqi, Pm25 = pm };
        }
        catch (Exception e) { Log.Write("weather: air quality: " + e.Message); }
        return w;
    }

    /// <summary>The air quality answer: European AQI and PM2.5 (null where missing).</summary>
    public static (double? Aqi, double? Pm25) ParseAirQuality(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("current", out var c)) return (null, null);
        static double? N(JsonElement o, string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        return (N(c, "european_aqi"), N(c, "pm2_5"));
    }

    /// <summary>The forecast answer to <see cref="WeatherData"/>. Times stay in the place's own time zone.</summary>
    public static WeatherData ParseOpenMeteo(string json, string placeName)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var cur = root.GetProperty("current");
        var daily = root.GetProperty("daily");
        var hourly = root.GetProperty("hourly");

        var now = DateTime.Parse(cur.GetProperty("time").GetString()!, Inv);
        var times = hourly.GetProperty("time");
        var temps = hourly.GetProperty("temperature_2m");
        var codes = hourly.GetProperty("weather_code");
        var days = hourly.GetProperty("is_day");

        var hours = new List<HourForecast>();
        for (int i = 0; i < times.GetArrayLength() && hours.Count < 6; i++)
        {
            var t = DateTime.Parse(times[i].GetString()!, Inv);
            if (t <= now) continue;
            hours.Add(new HourForecast(t, temps[i].GetDouble(), codes[i].GetInt32(), days[i].GetInt32() == 1));
        }

        var hourly7 = new List<WeatherHour>();
        for (int i = 0; i < times.GetArrayLength(); i++)
            hourly7.Add(new WeatherHour(DateTime.Parse(times[i].GetString()!, Inv), Num(hourly, "temperature_2m", i), Num(hourly, "precipitation", i),
                                        Num(hourly, "precipitation_probability", i), Num(hourly, "wind_speed_10m", i), Num(hourly, "uv_index", i),
                                        (int)Num(hourly, "weather_code", i), Num(hourly, "is_day", i) == 1));
        var quarters = new List<WeatherQuarter>();
        if (root.TryGetProperty("minutely_15", out var m15) && m15.TryGetProperty("time", out var qt))
            for (int i = 0; i < qt.GetArrayLength(); i++)
                quarters.Add(new WeatherQuarter(DateTime.Parse(qt[i].GetString()!, Inv), Num(m15, "precipitation", i)));
        var days7 = new List<WeatherDay>();
        var dt = daily.GetProperty("time");
        for (int i = 0; i < dt.GetArrayLength(); i++)
            days7.Add(new WeatherDay(DateTime.Parse(dt[i].GetString()!, Inv), Num(daily, "temperature_2m_min", i), Num(daily, "temperature_2m_max", i),
                                     (int)Num(daily, "weather_code", i), Num(daily, "precipitation_probability_max", i)));
        var offset = TimeSpan.FromSeconds(root.TryGetProperty("utc_offset_seconds", out var off) && off.ValueKind == JsonValueKind.Number ? off.GetDouble() : 0);

        return new WeatherData(
            placeName,
            cur.GetProperty("temperature_2m").GetDouble(),
            cur.GetProperty("apparent_temperature").GetDouble(),
            cur.GetProperty("weather_code").GetInt32(),
            cur.GetProperty("is_day").GetInt32() == 1,
            cur.GetProperty("relative_humidity_2m").GetDouble(),
            cur.GetProperty("surface_pressure").GetDouble() * 0.750062,
            cur.GetProperty("wind_speed_10m").GetDouble(),
            cur.GetProperty("wind_direction_10m").GetDouble(),
            DateTime.Parse(daily.GetProperty("sunrise")[0].GetString()!, Inv),
            DateTime.Parse(daily.GetProperty("sunset")[0].GetString()!, Inv),
            daily.GetProperty("temperature_2m_max")[0].GetDouble(),
            daily.GetProperty("temperature_2m_min")[0].GetDouble(),
            Num(cur, "uv_index"),
            Num(cur, "precipitation"),
            Num(daily, "precipitation_sum", 0),
            Num(daily, "precipitation_probability_max", 0),
            hours) { Hourly = hourly7, Minutely = quarters, Days = days7, UtcOffset = offset };
    }

    // ------------------------------------------------------------------ the last weather, on disk (shown offline)

    private static readonly string CacheFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "weather.json");

    public static void Save(WeatherData w)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
            File.WriteAllText(CacheFile + ".tmp", JsonSerializer.Serialize(w));
            File.Move(CacheFile + ".tmp", CacheFile, overwrite: true);
        }
        catch (Exception e) { Log.Write("weather cache write: " + e.Message); }
    }

    /// <summary>The weather saved last time for this city, however old (the widgets say how old).</summary>
    public static WeatherData? LoadSaved(string city)
    {
        try
        {
            if (!File.Exists(CacheFile)) return null;
            var w = JsonSerializer.Deserialize<WeatherData>(File.ReadAllText(CacheFile));
            return w != null && string.Equals(w.City, city.Trim(), StringComparison.OrdinalIgnoreCase) ? w : null;
        }
        catch (Exception e) { Log.Write("weather cache read: " + e.Message); return null; }
    }

    // ------------------------------------------------------------------ MET Norway (the fallback)

    private static async Task<WeatherData> MetNoAsync(Place place)
    {
        var url = "https://api.met.no/weatherapi/locationforecast/2.0/compact?lat=" + Math.Round(place.Lat, 4).ToString(Inv) +
                  "&lon=" + Math.Round(place.Lon, 4).ToString(Inv);
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var series = doc.RootElement.GetProperty("properties").GetProperty("timeseries");
        var first = series[0].GetProperty("data");
        var d = first.GetProperty("instant").GetProperty("details");
        double temp = Num(d, "air_temperature");
        double wind = Num(d, "wind_speed");

        var today = DateTime.Now.Date;
        var (rise, set) = SunTimes(place.Lat, place.Lon, today);
        bool IsDay(DateTime t) => (t.TimeOfDay >= rise.TimeOfDay && t.TimeOfDay < set.TimeOfDay) || rise == set;

        double max = double.MinValue, min = double.MaxValue, precipToday = 0;
        var byDay = new Dictionary<DateTime, List<(double T, int Code)>>();
        var hours = new List<HourForecast>();
        var hourly = new List<WeatherHour>();
        int codeNow = 3;
        var now = DateTime.Now;
        foreach (var item in series.EnumerateArray())
        {
            var t = DateTime.Parse(item.GetProperty("time").GetString()!, Inv, DateTimeStyles.AdjustToUniversal).ToLocalTime();
            var data = item.GetProperty("data");
            double tt = Num(data.GetProperty("instant").GetProperty("details"), "air_temperature");
            int code = 3;
            double precip = 0;
            if (data.TryGetProperty("next_1_hours", out var n1))
            {
                code = SymbolToWmo(n1.GetProperty("summary").GetProperty("symbol_code").GetString());
                if (n1.TryGetProperty("details", out var pd)) precip = Num(pd, "precipitation_amount");
            }
            else if (data.TryGetProperty("next_6_hours", out var n6))
                code = SymbolToWmo(n6.GetProperty("summary").GetProperty("symbol_code").GetString());
            if (item.GetProperty("time").GetString() == series[0].GetProperty("time").GetString()) codeNow = code;
            if (t.Date == today) { max = Math.Max(max, tt); min = Math.Min(min, tt); precipToday += precip; }
            if (t > now && hours.Count < 6) hours.Add(new HourForecast(t, tt, code, IsDay(t)));
            // MET Norway gives hours for about two days, then 6-hour steps: the chart takes the hourly part
            if (data.TryGetProperty("next_1_hours", out _))
                hourly.Add(new WeatherHour(t, tt, precip, 0, Num(data.GetProperty("instant").GetProperty("details"), "wind_speed"), 0, code, IsDay(t)));
            byDay.TryAdd(t.Date, []);
            byDay[t.Date].Add((tt, code));
        }
        var days = byDay.Where(p => p.Value.Count >= 2).OrderBy(p => p.Key).Take(7)
                        .Select(p => new WeatherDay(p.Key, p.Value.Min(x => x.T), p.Value.Max(x => x.T), p.Value.Max(x => x.Code), 0)).ToList();
        if (max == double.MinValue) { max = temp; min = temp; }
        double precipNow = first.TryGetProperty("next_1_hours", out var nn) && nn.TryGetProperty("details", out var nd) ? Num(nd, "precipitation_amount") : 0;

        return new WeatherData(
            place.Name, temp, FeelsLike(temp, wind), codeNow, IsDay(now),
            Num(d, "relative_humidity"), Num(d, "air_pressure_at_sea_level") * 0.750062, wind, Num(d, "wind_from_direction"),
            rise, set, max, min, 0, precipNow, precipToday, 0, hours) { Hourly = hourly, Days = days };
    }

    /// <summary>MET Norway symbol ("lightrainshowers_day") to the WMO code Open-Meteo uses.</summary>
    private static int SymbolToWmo(string? symbol)
    {
        var s = (symbol ?? "").Split('_')[0];
        if (s.Contains("thunder")) return 95;
        return s switch
        {
            "clearsky" => 0,
            "fair" => 1,
            "partlycloudy" => 2,
            "cloudy" => 3,
            "fog" => 45,
            "lightrainshowers" => 80,
            "rainshowers" => 81,
            "heavyrainshowers" => 82,
            "lightrain" => 61,
            "rain" => 63,
            "heavyrain" => 65,
            "lightsleet" or "lightsleetshowers" => 66,
            "sleet" or "heavysleet" or "sleetshowers" or "heavysleetshowers" => 67,
            "lightsnowshowers" or "snowshowers" => 85,
            "heavysnowshowers" => 86,
            "lightsnow" => 71,
            "snow" => 73,
            "heavysnow" => 75,
            _ => 3,
        };
    }

    /// <summary>Wind chill when it's cold and windy (the same idea as Open-Meteo's "apparent temperature").</summary>
    private static double FeelsLike(double t, double windMs)
    {
        double v = windMs * 3.6;
        if (t > 10 || v < 4.8) return t;
        double p = Math.Pow(v, 0.16);
        return 13.12 + 0.6215 * t - 11.37 * p + 0.3965 * t * p;
    }

    /// <summary>Sunrise and sunset (local time) by the NOAA formula — good to a minute or two.</summary>
    private static (DateTime Rise, DateTime Set) SunTimes(double lat, double lon, DateTime date)
    {
        int n = date.DayOfYear;
        double g = 2 * Math.PI / 365 * (n - 1);
        double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g) - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
        double decl = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g) + 0.000907 * Math.Sin(2 * g)
                      - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);
        double la = lat * Math.PI / 180;
        double cosHa = Math.Cos(90.833 * Math.PI / 180) / (Math.Cos(la) * Math.Cos(decl)) - Math.Tan(la) * Math.Tan(decl);
        if (cosHa >= 1) return (date, date);                       // polar night
        if (cosHa <= -1) return (date, date.AddDays(1).AddTicks(-1)); // polar day
        double ha = Math.Acos(cosHa) * 180 / Math.PI;
        var utcMidnight = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
        var rise = utcMidnight.AddMinutes(720 - 4 * (lon + ha) - eqTime).ToLocalTime();
        var set = utcMidnight.AddMinutes(720 - 4 * (lon - ha) - eqTime).ToLocalTime();
        return (rise, set);
    }

    private static double Num(JsonElement obj, string name, int? index = null)
    {
        if (!obj.TryGetProperty(name, out var v)) return 0;
        if (index is int i) v = v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > i ? v[i] : default;
        return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    }

    // WMO weather codes -> text / glyph (Segoe UI Symbol).
    public static string Describe(int code) => code switch
    {
        0 => L.T("Ясно"),
        1 => L.T("Малооблачно"),
        2 => L.T("Переменная облачность"),
        3 => L.T("Пасмурно"),
        45 or 48 => L.T("Туман"),
        >= 51 and <= 57 => L.T("Морось"),
        >= 61 and <= 67 => L.T("Дождь"),
        >= 71 and <= 77 => L.T("Снег"),
        >= 80 and <= 82 => L.T("Ливень"),
        85 or 86 => L.T("Снегопад"),
        >= 95 => L.T("Гроза"),
        _ => "—",
    };

    public static string Glyph(int code, bool isDay) => code switch
    {
        0 or 1 => isDay ? "☀" : "☾",
        2 => isDay ? "⛅" : "☁",
        3 => "☁",
        45 or 48 => "≋",
        >= 51 and <= 67 => "☂",
        >= 80 and <= 82 => "☂",
        >= 71 and <= 77 => "❄",
        85 or 86 => "❄",
        >= 95 => "⛈",
        _ => "☁",
    };

    public static string Compass(double degrees)
    {
        string[] names = [L.T("С"), L.T("СВ"), L.T("В"), L.T("ЮВ"), L.T("Ю"), L.T("ЮЗ"), L.T("З"), L.T("СЗ")];
        return names[(int)Math.Round(((degrees % 360) + 360) % 360 / 45) % 8];
    }
}

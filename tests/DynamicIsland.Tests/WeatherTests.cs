using System.IO;
using System.Text.Json;
using DynamicIsland.Services;
using DynamicIsland.Views;
using Xunit;

namespace DynamicIsland.Tests;

public class WeatherParseTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ParsesARealOpenMeteoAnswer()
    {
        var w = WeatherService.ParseOpenMeteo(Fixture("open-meteo-odesa.json"), "Одесса");
        Assert.Equal("Одесса", w.Place);
        Assert.Equal(11.4, w.Temp);
        Assert.Equal(9.4, w.FeelsLike);
        Assert.Equal(61, w.Code);
        Assert.True(w.IsDay);
        Assert.Equal(3.14, w.WindSpeed);
        Assert.Equal(TimeSpan.FromHours(2), w.UtcOffset);
        Assert.Equal(168, w.Hourly.Count);                         // 7 days × 24 h
        Assert.Equal(new DateTime(2026, 11, 18, 0, 0, 0), w.Hourly[0].Time);
        Assert.Equal(24, w.Minutely.Count);                        // 6 hours in quarters
        Assert.Equal(new DateTime(2026, 11, 18, 10, 15, 0), w.Minutely[0].Time);
        Assert.Equal(7, w.Days.Count);
        Assert.Equal(7.7, w.Days[0].Min); Assert.Equal(13.1, w.Days[0].Max); Assert.Equal(63, w.Days[0].Chance);
        Assert.Equal(new DateTime(2026, 11, 18, 7, 2, 0), w.Sunrise);
        Assert.Equal(new DateTime(2026, 11, 18, 16, 21, 0), w.Sunset);
        Assert.All(w.Hourly, h => Assert.InRange(h.Chance, 0, 100));
        Assert.True(w.Hours.Count <= 6 && w.Hours.All(h => h.Time > new DateTime(2026, 11, 18, 10, 15, 0)));
    }

    [Fact]
    public void ParsesAirQuality()
    {
        var (aqi, pm) = WeatherService.ParseAirQuality(Fixture("air-quality-odesa.json"));
        Assert.Equal(21, aqi);
        Assert.Equal(5.3, pm);
        Assert.Equal((null, null), WeatherService.ParseAirQuality("{\"current\":{\"time\":\"2026-11-18T10:00\"}}"));
        Assert.Equal((null, null), WeatherService.ParseAirQuality("{}"));
    }

    [Fact]
    public void SurvivesTheDiskRoundTrip()
    {
        var w = WeatherService.ParseOpenMeteo(Fixture("open-meteo-odesa.json"), "Одесса") with { Aqi = 21, City = "Одесса", FetchedAt = new DateTime(2026, 11, 18, 10, 20, 0) };
        var back = JsonSerializer.Deserialize<WeatherData>(JsonSerializer.Serialize(w))!;
        Assert.Equal(w.Temp, back.Temp);
        Assert.Equal(w.Hourly.Count, back.Hourly.Count);
        Assert.Equal(w.Minutely[3], back.Minutely[3]);
        Assert.Equal(w.Days[2], back.Days[2]);
        Assert.Equal(21, back.Aqi);
        Assert.Equal("Одесса", back.City);
        Assert.Equal(w.FetchedAt, back.FetchedAt);
        Assert.Equal(w.UtcOffset, back.UtcOffset);
    }

    [Fact]
    public void SunProgress()
    {
        var rise = new DateTime(2026, 11, 18, 7, 0, 0); var set = new DateTime(2026, 11, 18, 17, 0, 0);
        Assert.Equal(0, SunArc.Progress(rise, set, rise));
        Assert.Equal(0.5, SunArc.Progress(rise, set, new DateTime(2026, 11, 18, 12, 0, 0)));
        Assert.True(SunArc.Progress(rise, set, new DateTime(2026, 11, 18, 22, 0, 0)) > 1);
        Assert.Equal(-1, SunArc.Progress(rise, rise, rise));
    }
}

public class WeatherAdviceTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 14, 0, 0);

    /// <summary>A dry, calm afternoon; tests change one thing at a time.</summary>
    private static WeatherData Day(double temp = 10, int code = 2, double wind = 3, double precipNow = 0,
                                   Func<DateTime, double>? quarterRain = null, Func<DateTime, (double Precip, double Chance, double Wind)>? hour = null)
    {
        var quarters = Enumerable.Range(1, 24).Select(i => Now.AddMinutes(15 * i)).Select(t => new WeatherQuarter(t, quarterRain?.Invoke(t) ?? 0)).ToList();
        var hours = Enumerable.Range(-14, 48).Select(i => Now.AddHours(i)).Select(t =>
        {
            var (p, c, wd) = hour?.Invoke(t) ?? (0, 5, wind);
            return new WeatherHour(t, temp, p, c, wd, 1, code, t.Hour is > 7 and < 17);
        }).ToList();
        return new WeatherData("Тест", temp, temp, code, true, 70, 750, wind, 180, Now.Date.AddHours(7), Now.Date.AddHours(16.5), temp + 2, temp - 2, 1, precipNow, 0, 0, [])
        { Hourly = hours, Minutely = quarters };
    }

    [Fact]
    public void RainStartingFromTheQuarters()
    {
        var w = Day(quarterRain: t => t >= Now.AddMinutes(100) ? 0.4 : 0);   // the quarter 15:30–15:45 is the first wet one
        Assert.Equal("Дождь начнётся около 15:30", WeatherAdvice.Build(w, Now));
    }

    [Fact]
    public void RainInTheNextMinutes()
    {
        var w = Day(quarterRain: t => t >= Now.AddMinutes(30) ? 0.3 : 0);   // 14:15–14:30
        Assert.Equal("Дождь начнётся в ближайшие минуты", WeatherAdvice.Build(w, Now));
        var already = Day(quarterRain: t => t >= Now.AddMinutes(15) && t < Now.AddMinutes(60) ? 0.3 : 0);   // 14:00–14:45: raining now
        Assert.Equal("Дождь закончится около 14:45", WeatherAdvice.Build(already, Now));
    }

    [Fact]
    public void SnowWhenCold()
    {
        var w = Day(temp: -3, quarterRain: t => t >= Now.AddMinutes(60) ? 0.2 : 0);
        Assert.Equal("Снег начнётся около 14:45", WeatherAdvice.Build(w, Now));
    }

    [Fact]
    public void RainingNowStopsLater()
    {
        var w = Day(precipNow: 0.5, code: 63, quarterRain: t => t < Now.AddMinutes(75) ? 0.5 : 0);
        Assert.Equal("Дождь закончится около 15:00", WeatherAdvice.Build(w, Now));
    }

    [Fact]
    public void RainingAllDay()
    {
        var w = Day(precipNow: 1, code: 63, quarterRain: _ => 1, hour: _ => (2, 90, 3));
        Assert.Equal("Дождь надолго, до конца дня", WeatherAdvice.Build(w, Now));
    }

    [Fact]
    public void RainLaterFromTheHours()
    {
        var w = Day(hour: t => t >= Now.AddHours(9) ? (0.1, 70, 3) : (0, 10, 3));   // 23:00, likely
        Assert.Equal("Дождь начнётся около 23:00", WeatherAdvice.Build(w, Now));
        var maybe = Day(hour: t => t >= Now.AddHours(6) ? (0, 45, 3) : (0, 10, 3));
        Assert.Equal("Возможен дождь около 20:00", WeatherAdvice.Build(maybe, Now));
    }

    [Fact]
    public void WindPickingUp()
    {
        var w = Day(hour: t => (0, 5, t >= Now.AddHours(13) ? 3 : t >= Now.AddHours(5) ? 12 : 4));
        Assert.Equal("Ветер усилится до 12 м/с после 19:00", WeatherAdvice.Build(w, Now));
        var always = Day(wind: 11, hour: _ => (0, 5, 12));                         // windy already: nothing new to say
        Assert.Equal("Сухо до ночи", WeatherAdvice.Build(always, Now));
    }

    [Theory]
    [InlineData(9, "Сухо весь день")]
    [InlineData(14, "Сухо до ночи")]
    [InlineData(20, "Сухо до утра")]
    [InlineData(2, "Сухо до утра")]
    public void DryDependsOnTheTimeOfDay(int hour, string expected)
    {
        var w = Day();
        var at = Now.Date.AddHours(hour);
        Assert.Equal(expected, WeatherAdvice.Build(w, at));
    }

    [Fact]
    public void NoForecastFallsBackToTheSky()
    {
        var w = Day() with { Hourly = [], Minutely = [] };
        Assert.Equal(WeatherService.Describe(2), WeatherAdvice.Build(w, Now));
    }
}

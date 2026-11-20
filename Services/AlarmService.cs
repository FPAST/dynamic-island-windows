namespace DynamicIsland.Services;

/// <summary>An alarm: a time of day, the days it rings on (none = once), on or off.</summary>
public sealed class Alarm
{
    public TimeSpan Time { get; set; } = new(7, 0, 0);
    /// <summary>Bits: Monday = 1, Tuesday = 2 … Sunday = 64. 0 rings once and then turns itself off.</summary>
    public int Days { get; set; }
    public bool Enabled { get; set; } = true;
    public string Label { get; set; } = "";
    /// <summary>When it last rang (it rings at most once a day).</summary>
    public DateTime LastRang { get; set; }
}

/// <summary>When alarms ring. Pure: the caller gives the time; the island plays the sound and shows the note.</summary>
public static class AlarmService
{
    /// <summary>A missed ring (the PC slept through it) still sounds if it is at most this late.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);

    public const int Weekdays = 0b0011111, Weekend = 0b1100000, Everyday = 0b1111111;

    public static int DayBit(DayOfWeek d) => 1 << ((int)d + 6) % 7;   // Monday first

    /// <summary>Should it ring now? Then the caller calls <see cref="Rang"/>.</summary>
    public static bool IsDue(Alarm a, DateTime now)
    {
        if (!a.Enabled || a.LastRang.Date == now.Date && a.LastRang.TimeOfDay >= a.Time) return false;
        if (a.Days != 0 && (a.Days & DayBit(now.DayOfWeek)) == 0) return false;
        var late = now.TimeOfDay - a.Time;
        return late >= TimeSpan.Zero && late <= Grace;
    }

    public static void Rang(Alarm a, DateTime now)
    {
        a.LastRang = now;
        if (a.Days == 0) a.Enabled = false;   // a one-time alarm is done
    }

    /// <summary>The next time it rings, or null when it is off.</summary>
    public static DateTime? Next(Alarm a, DateTime now)
    {
        if (!a.Enabled) return null;
        for (int d = 0; d <= 7; d++)
        {
            var day = now.Date.AddDays(d);
            var at = day + a.Time;
            if (at <= now) continue;
            if (a.Days != 0 && (a.Days & DayBit(day.DayOfWeek)) == 0) continue;
            return at;
        }
        return null;
    }

    /// <summary>The soonest ring of all alarms.</summary>
    public static DateTime? Soonest(IEnumerable<Alarm> alarms, DateTime now) =>
        alarms.Select(a => Next(a, now)).Where(t => t != null).Min();

    /// <summary>"через 7 ч 20 мин", "через 5 мин", "меньше чем через минуту".</summary>
    public static string Until(TimeSpan t)
    {
        if (t < TimeSpan.FromMinutes(1)) return L.T("меньше чем через минуту");
        int h = (int)t.TotalHours, m = (int)Math.Ceiling(t.TotalMinutes - h * 60);
        if (m == 60) { h++; m = 0; }
        if (h == 0) return L.F("через {0} мин", m);
        return m == 0 ? L.F("через {0} ч", h) : L.F("через {0} ч {1} мин", h, m);
    }

    /// <summary>"Каждый день", "По будням", "По выходным", "Однократно" or the days ("пн, ср, пт").</summary>
    public static string DaysText(int days, System.Globalization.CultureInfo culture)
    {
        if (days == 0) return L.T("Однократно");
        if (days == Everyday) return L.T("Каждый день");
        if (days == Weekdays) return L.T("По будням");
        if (days == Weekend) return L.T("По выходным");
        var names = new List<string>();
        for (int i = 0; i < 7; i++)
            if ((days & (1 << i)) != 0) names.Add(culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((i + 1) % 7)));
        return string.Join(", ", names);
    }
}

/// <summary>Pomodoro sessions finished today (it starts from zero every day).</summary>
public static class PomodoroCount
{
    public static int Today(DateTime countedOn, int count, DateTime now) => countedOn.Date == now.Date ? count : 0;

    /// <summary>After a work session: (the day it counts for, the new count).</summary>
    public static (DateTime Day, int Count) Add(DateTime countedOn, int count, DateTime now) => (now.Date, Today(countedOn, count, now) + 1);
}

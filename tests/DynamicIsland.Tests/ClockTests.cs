using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class AlarmTests
{
    // 2026-11-18 is a Wednesday
    private static readonly DateTime Wed = new(2026, 11, 18);
    private static Alarm At(int h, int m, int days = 0) => new() { Time = new TimeSpan(h, m, 0), Days = days };

    [Fact]
    public void DayBitsStartOnMonday()
    {
        Assert.Equal(1, AlarmService.DayBit(DayOfWeek.Monday));
        Assert.Equal(4, AlarmService.DayBit(DayOfWeek.Wednesday));
        Assert.Equal(64, AlarmService.DayBit(DayOfWeek.Sunday));
        Assert.NotEqual(0, AlarmService.Weekdays & AlarmService.DayBit(DayOfWeek.Friday));
        Assert.Equal(0, AlarmService.Weekdays & AlarmService.DayBit(DayOfWeek.Saturday));
    }

    [Fact]
    public void RingsAtItsTimeOnceAndWithinGrace()
    {
        var a = At(7, 0, AlarmService.Everyday);
        Assert.False(AlarmService.IsDue(a, Wed.AddHours(6.99)));
        Assert.True(AlarmService.IsDue(a, Wed.AddHours(7)));
        Assert.True(AlarmService.IsDue(a, Wed.AddHours(7).AddMinutes(9)));    // the PC woke up a bit late
        Assert.False(AlarmService.IsDue(a, Wed.AddHours(7).AddMinutes(11)));  // too late: not at 7:11
        AlarmService.Rang(a, Wed.AddHours(7));
        Assert.False(AlarmService.IsDue(a, Wed.AddHours(7).AddSeconds(30)));  // once a day
        Assert.True(AlarmService.IsDue(a, Wed.AddDays(1).AddHours(7)));       // and again tomorrow
        Assert.True(a.Enabled);
    }

    [Fact]
    public void WeekdaysOnlyOnWeekdays()
    {
        var a = At(8, 30, AlarmService.Weekdays);
        Assert.True(AlarmService.IsDue(a, Wed.AddHours(8.5)));
        Assert.False(AlarmService.IsDue(a, Wed.AddDays(3).AddHours(8.5)));    // Saturday
        Assert.Equal(Wed.AddDays(5).AddHours(8.5), AlarmService.Next(a, Wed.AddDays(2).AddHours(9)));   // Friday after 8:30 → Monday
    }

    [Fact]
    public void OnceTurnsItselfOff()
    {
        var a = At(6, 0);
        Assert.True(AlarmService.IsDue(a, Wed.AddHours(6)));
        AlarmService.Rang(a, Wed.AddHours(6));
        Assert.False(a.Enabled);
        Assert.Null(AlarmService.Next(a, Wed.AddHours(7)));
        Assert.False(AlarmService.IsDue(a, Wed.AddDays(1).AddHours(6)));
    }

    [Fact]
    public void ChangingTheTimeLaterTheSameDayRingsAgain()
    {
        var a = At(7, 0, AlarmService.Everyday);
        AlarmService.Rang(a, Wed.AddHours(7));
        a.Time = new TimeSpan(9, 0, 0);
        Assert.True(AlarmService.IsDue(a, Wed.AddHours(9)));
    }

    [Fact]
    public void NextAndSoonest()
    {
        var now = Wed.AddHours(22);
        var morning = At(7, 0, AlarmService.Everyday);
        var off = At(23, 0); off.Enabled = false;
        Assert.Equal(Wed.AddDays(1).AddHours(7), AlarmService.Next(morning, now));
        Assert.Null(AlarmService.Next(off, now));
        Assert.Equal(Wed.AddDays(1).AddHours(7), AlarmService.Soonest([off, morning, At(23, 30)], now.AddHours(1.6)));
        Assert.Equal(Wed.AddHours(23.5), AlarmService.Soonest([morning, At(23, 30)], now));
        Assert.Null(AlarmService.Soonest([], now));
    }

    [Theory]
    [InlineData(0.5, "меньше чем через минуту")]
    [InlineData(5 * 60, "через 5 мин")]
    [InlineData(4 * 60 + 30, "через 5 мин")]
    [InlineData(2 * 3600, "через 2 ч")]
    [InlineData(7 * 3600 + 20 * 60, "через 7 ч 20 мин")]
    [InlineData(59 * 60 + 50, "через 1 ч")]
    public void Until(double seconds, string text) => Assert.Equal(text, AlarmService.Until(TimeSpan.FromSeconds(seconds)));
}

public class PomodoroCountTests
{
    [Fact]
    public void CountsTodayOnlyAndStartsOverTomorrow()
    {
        var d = new DateTime(2026, 11, 18, 10, 0, 0);
        Assert.Equal(0, PomodoroCount.Today(default, 0, d));
        var (day, n) = PomodoroCount.Add(default, 0, d);
        (day, n) = PomodoroCount.Add(day, n, d.AddHours(1));
        Assert.Equal(2, PomodoroCount.Today(day, n, d.AddHours(3)));
        Assert.Equal(0, PomodoroCount.Today(day, n, d.AddDays(1)));
        Assert.Equal((d.Date.AddDays(1), 1), PomodoroCount.Add(day, n, d.AddDays(1)));
    }
}

public class TimerServiceTests
{
    [Fact]
    public void PomodoroUsesTheChosenLengthsAndReportsFinishedWork()
    {
        var keep = (TimerService.Work, TimerService.ShortBreak);
        try
        {
            TimerService.Work = TimeSpan.FromMilliseconds(30);
            TimerService.ShortBreak = TimeSpan.FromMinutes(7);
            var t = new TimerService();
            int done = 0; string? said = null;
            t.WorkDone += () => done++;
            t.Finished += (_, text) => said = text;
            t.SetKind(TimerKind.Pomodoro);
            Assert.Equal(TimeSpan.FromMilliseconds(30), t.Duration);
            t.Start();
            Thread.Sleep(60);
            t.Tick();
            Assert.Equal(1, done);
            Assert.False(t.PomodoroWork);
            Assert.Equal(TimeSpan.FromMinutes(7), t.Duration);
            Assert.Contains("7", said);
        }
        finally { (TimerService.Work, TimerService.ShortBreak) = keep; }
    }
}

using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class SystemHistoryTests
{
    [Fact]
    public void SeriesKeepsTheLastValuesOldestFirst()
    {
        var s = new Series(3);
        Assert.Empty(s.ToArray());
        Assert.Equal(0, s.Last);
        s.Add(1); s.Add(2);
        Assert.Equal([1.0, 2.0], s.ToArray());
        s.Add(3); s.Add(4); s.Add(5);
        Assert.Equal([3.0, 4.0, 5.0], s.ToArray());
        Assert.Equal(5, s.Last);
        Assert.Equal(3, s.Count);
    }
}

public class OverheatTests
{
    private static readonly DateTime T = new(2026, 11, 19, 12, 0, 0);

    [Fact]
    public void WarnsOnlyAfterHalfAMinuteOfHeat()
    {
        var w = new OverheatWatch();
        Assert.False(w.Update(92, T));
        Assert.False(w.Update(93, T.AddSeconds(20)));
        Assert.False(w.Warning);
        Assert.True(w.Update(91, T.AddSeconds(31)));   // starts: told once
        Assert.True(w.Warning);
        Assert.False(w.Update(95, T.AddSeconds(40)));  // still on, not told again
        Assert.True(w.Warning);
    }

    [Fact]
    public void AShortPeakIsNoWarning()
    {
        var w = new OverheatWatch();
        w.Update(94, T);
        w.Update(80, T.AddSeconds(10));               // cooled down: the half minute starts over
        Assert.False(w.Update(94, T.AddSeconds(35)));
        Assert.False(w.Warning);
    }

    [Fact]
    public void ClearsBelowEightyFiveOnly()
    {
        var w = new OverheatWatch();
        w.Update(92, T); w.Update(92, T.AddSeconds(31));
        w.Update(88, T.AddSeconds(40));
        Assert.True(w.Warning);                        // 88°: between the lines, stays (no blinking)
        w.Update(84, T.AddSeconds(50));
        Assert.False(w.Warning);
    }

    [Fact]
    public void NoSensorNoWarning()
    {
        var w = new OverheatWatch();
        w.Update(95, T); w.Update(95, T.AddSeconds(40));
        Assert.True(w.Warning);
        w.Update(null, T.AddSeconds(41));
        Assert.False(w.Warning);
    }
}

public class CpuTopTests
{
    private static Dictionary<int, (string, TimeSpan)> Snap(params (int Pid, string Name, double Sec)[] p) =>
        p.ToDictionary(x => x.Pid, x => (x.Name, TimeSpan.FromSeconds(x.Sec)));

    [Fact]
    public void SharesOfAllCoresPerProgramName()
    {
        var before = Snap((1, "chrome", 10), (2, "chrome", 5), (3, "game", 100), (4, "idle-ish", 1));
        var after = Snap((1, "chrome", 11), (2, "chrome", 6), (3, "game", 104), (4, "idle-ish", 1));
        // 2 s on 4 cores = 8 core-seconds: game 4 → 50%, chrome 1+1 → 25%
        var top = CpuTop.Top(before, after, 2, 4, 5);
        Assert.Equal("game", top[0].Name); Assert.Equal(50, top[0].Percent, 6);
        Assert.Equal("chrome", top[1].Name); Assert.Equal(25, top[1].Percent, 6);
        Assert.Equal(2, top.Count);   // nothing used: not listed
    }

    [Fact]
    public void NewProcessesAndReusedIdsAreSkipped()
    {
        var before = Snap((1, "old", 5));
        var after = Snap((1, "new-with-old-id", 50), (2, "fresh", 3));
        Assert.Empty(CpuTop.Top(before, after, 2, 4, 5));
    }

    [Fact]
    public void LimitsAndGuards()
    {
        var before = Snap((1, "a", 0), (2, "b", 0), (3, "c", 0));
        var after = Snap((1, "a", 3), (2, "b", 2), (3, "c", 1));
        Assert.Equal(["a", "b"], CpuTop.Top(before, after, 1, 8, 2).Select(e => e.Name));
        Assert.Empty(CpuTop.Top(before, after, 0, 8, 2));
        Assert.Equal(100, CpuTop.Top(before, Snap((1, "a", 100)), 1, 1, 1)[0].Percent);   // capped
    }
}

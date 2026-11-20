using System.IO;
using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class EqualizerApoTests
{
    private static readonly double[] Rock = [5, 4, 2, -1, -2, -1, 2, 3, 4, 4];

    [Fact]
    public void BuildsPreampAndGraphicEq()
    {
        var text = EqualizerApo.Build(Rock, bypass: false);
        Assert.Contains("Preamp: -5 dB\r\n", text);
        Assert.Contains("GraphicEQ: 31 5; 62 4; 125 2; 250 -1; 500 -2; 1000 -1; 2000 2; 4000 3; 8000 4; 16000 4\r\n", text);
    }

    [Fact]
    public void PreampIsMinusTheLargestBoost_ZeroWhenOnlyCuts()
    {
        Assert.Contains("Preamp: -12 dB", EqualizerApo.Build([12, 0, 0, 0, 0, 0, 0, 0, 0, 3.5], false));
        var cuts = EqualizerApo.Build([-3, -6, 0, 0, 0, 0, 0, 0, 0, 0], false);
        Assert.Contains("Preamp: 0 dB", cuts);
        Assert.DoesNotContain("-0", cuts.Split('\n')[1]);
    }

    [Fact]
    public void ClampsToTwelveAndUsesInvariantDecimals()
    {
        var text = EqualizerApo.Build([20, -30, 1.5, 0, 0, 0, 0, 0, 0, 0], false);
        Assert.Contains("31 12; 62 -12; 125 1.5", text);   // always with a dot, whatever the system's culture
        Assert.Contains("Preamp: -12 dB", text);
        Assert.DoesNotContain(",5", text);
    }

    [Fact]
    public void BypassAndFlatWriteNoFilters()
    {
        foreach (var t in new[] { EqualizerApo.Build(Rock, bypass: true), EqualizerApo.Build(new double[10], false) })
        {
            Assert.DoesNotContain("GraphicEQ", t);
            Assert.DoesNotContain("Preamp", t);
        }
    }

    [Fact]
    public void WrongBandCountIsRefused() => Assert.Throws<ArgumentException>(() => EqualizerApo.Build([1, 2, 3], false));

    [Fact]
    public void IncludeIsAddedOnceAndKeepsTheUsersText()
    {
        var user = "Preamp: -6 dB\r\nInclude: example.txt\r\nGraphicEQ: 25 0; 40 0";
        var once = EqualizerApo.WithInclude(user);
        Assert.Equal(user + "\r\nInclude: WidgetEQ.txt\r\n", once);
        Assert.Null(EqualizerApo.WithInclude(once!));
        Assert.Null(EqualizerApo.WithInclude("  include:   widgeteq.txt  \n"));   // any case and spacing counts
        Assert.Equal("a\nInclude: WidgetEQ.txt\n", EqualizerApo.WithInclude("a\n"));
        Assert.Equal("Include: WidgetEQ.txt\n", EqualizerApo.WithInclude(""));
        // a different file that merely starts the same is not ours
        Assert.NotNull(EqualizerApo.WithInclude("Include: WidgetEQ.txt.old\n"));
    }

    [Fact]
    public void ApplyWritesOurFileAndOneIncludeWithABackup()
    {
        var dir = Directory.CreateTempSubdirectory("atoll-eq-").FullName;
        try
        {
            var user = "Preamp: -6 dB\r\nInclude: example.txt\r\n";
            File.WriteAllText(Path.Combine(dir, "config.txt"), user);
            var apo = new EqualizerApo(dir);
            Assert.False(apo.Included);
            Assert.Equal(EqualizerApo.Result.Ok, apo.Apply(Rock, false));
            Assert.Equal(EqualizerApo.Result.Ok, apo.Apply([1, 1, 1, 1, 1, 1, 1, 1, 1, 1], false));
            Assert.True(apo.Included);
            Assert.Equal(user + "Include: WidgetEQ.txt\r\n", File.ReadAllText(Path.Combine(dir, "config.txt")));
            Assert.Equal(user, File.ReadAllText(Path.Combine(dir, "config.txt.atoll-backup")));
            Assert.Contains("Preamp: -1 dB", File.ReadAllText(Path.Combine(dir, "WidgetEQ.txt")));
            Assert.False(File.Exists(Path.Combine(dir, "WidgetEQ.txt.tmp")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResponseGoesThroughEveryBandWithoutOvershoot()
    {
        for (int i = 0; i < EqualizerApo.Bands.Length; i++)
        {
            double x = Math.Log(EqualizerApo.Bands[i] / 20.0) / Math.Log(1000);
            Assert.Equal(Rock[i], EqualizerApo.ResponseAt(Rock, x), 6);
        }
        for (double x = 0; x <= 1; x += 0.01)
        {
            double r = EqualizerApo.ResponseAt(Rock, x);
            Assert.InRange(r, Rock.Min() - 1e-9, Rock.Max() + 1e-9);
        }
    }

    [Fact]
    public void PresetsHaveTenBandsWithinRange()
    {
        Assert.Equal(["Плоский", "Бас", "Вокал", "Рок", "Синти-поп"], EqualizerApo.Presets.Select(p => p.Name));
        foreach (var (_, g) in EqualizerApo.Presets)
        {
            Assert.Equal(10, g.Length);
            Assert.All(g, v => Assert.InRange(v, -EqualizerApo.MaxGain, EqualizerApo.MaxGain));
        }
    }
}

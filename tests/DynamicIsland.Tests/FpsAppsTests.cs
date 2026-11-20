using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class FpsAppsTests
{
    private static readonly string[] Listed = ["Cyberpunk2077", "javaw", @"D:\Games\Pirate\game.exe", @"D:\Games\Witcher"];

    [Theory]
    [InlineData("chrome")]
    [InlineData("msedge")]
    [InlineData("Firefox")]
    [InlineData("opera")]
    [InlineData("brave")]
    [InlineData("vivaldi")]
    [InlineData("browser")]
    [InlineData("explorer")]
    [InlineData("DynamicIsland")]
    public void NonGamesNeverCount(string name)
    {
        Assert.False(FpsMeter.CountsAsGame(name, "", false, Listed));
        Assert.False(FpsMeter.CountsAsGame(name, "", true, [name]));
        Assert.False(FpsMeter.CountsAsGame(name, @"D:\Games\Witcher\" + name + ".exe", true, Listed));
    }

    [Fact]
    public void EveryFullScreenProgramCountsByDefault()
    {
        Assert.True(FpsMeter.CountsAsGame("eldenring", "", false, []));
        Assert.True(FpsMeter.CountsAsGame("", "", false, []));
    }

    [Fact]
    public void OnlyListedProgramsCountInListedMode()
    {
        Assert.True(FpsMeter.CountsAsGame("cyberpunk2077", "", true, Listed));
        Assert.True(FpsMeter.CountsAsGame("javaw", @"C:\Java\bin\javaw.exe", true, Listed));
        Assert.False(FpsMeter.CountsAsGame("eldenring", @"D:\Games\Elden\eldenring.exe", true, Listed));
        Assert.False(FpsMeter.CountsAsGame("", "", true, Listed));
        Assert.False(FpsMeter.CountsAsGame("eldenring", "", true, []));
    }

    [Fact]
    public void ProgramPathMatchesOnlyThatProgram()
    {
        Assert.True(FpsMeter.CountsAsGame("game", @"d:\games\pirate\GAME.EXE", true, Listed));
        Assert.False(FpsMeter.CountsAsGame("launcher", @"D:\Games\Pirate\launcher.exe", true, Listed));
        Assert.False(FpsMeter.CountsAsGame("game", "", true, Listed));
    }

    [Fact]
    public void FolderMatchesEverythingInsideIt()
    {
        Assert.True(FpsMeter.CountsAsGame("Witcher3", @"D:\Games\Witcher\bin\x64\Witcher3.exe", true, Listed));
        Assert.True(FpsMeter.CountsAsGame("Witcher3", @"D:\Games\Witcher\bin\Witcher3.exe", true, [@"D:\Games\Witcher\"]));
        Assert.False(FpsMeter.CountsAsGame("Witcher3", @"D:\Games\WitcherOld\Witcher3.exe", true, Listed));
    }

    [Fact]
    public void NameEntryDoesNotMatchAPath()
    {
        Assert.False(AppList.Matches("game", "other", @"D:\Games\game\other.exe"));
        Assert.True(AppList.Matches("Zoom", "zoom", ""));
    }
}

using System.IO;
using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

[Collection("settings file")]
public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "atoll-settings-" + Guid.NewGuid().ToString("N"));
    private readonly string _was = Settings.FilePath, _logWas = Log.FilePath;

    public SettingsTests()
    {
        Directory.CreateDirectory(_dir);
        Settings.FilePath = Path.Combine(_dir, "settings.json");
        Log.FilePath = Path.Combine(_dir, "log.txt");   // never the user's own log
    }

    public void Dispose()
    {
        Settings.FilePath = _was;
        Log.FilePath = _logWas;
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void SaveGoesThroughATempFileAndLeavesNoneBehind()
    {
        var s = new Settings { City = "Одесса" };
        s.Save();
        Assert.True(File.Exists(Settings.FilePath));
        Assert.False(File.Exists(Settings.FilePath + ".tmp"));
        Assert.Equal("Одесса", Settings.Load().City);
    }

    [Fact]
    public void ALeftoverTempFileDoesNotStopTheNextSave()
    {
        File.WriteAllText(Settings.FilePath + ".tmp", "{ half written");
        new Settings { City = "Kyiv" }.Save();
        Assert.Equal("Kyiv", Settings.Load().City);
        Assert.False(File.Exists(Settings.FilePath + ".tmp"));
    }

    [Fact]
    public void SaveSoonWritesTheLastValueOnceTheSliderStops()
    {
        var s = new Settings();
        for (int i = 80; i <= 120; i += 5) { s.IslandScale = i; s.SaveSoon(); }
        Assert.False(File.Exists(Settings.FilePath));
        for (int t = 0; t < 40 && !File.Exists(Settings.FilePath); t++) Thread.Sleep(50);
        Assert.Equal(120, Settings.Load().IslandScale);
    }

    [Fact]
    public void FlushWritesAPendingSaveAtOnce()
    {
        var s = new Settings { IslandScale = 110 };
        s.SaveSoon();
        s.Flush();
        Assert.Equal(110, Settings.Load().IslandScale);
    }

    [Fact]
    public void SafeModeKeepsTheUsersOwnValuesInTheFile()
    {
        var s = new Settings { Karaoke = true, Roundness = 40 };
        s.EnterSafeMode();
        Assert.False(s.Karaoke);
        s.City = "Lviv";   // changed during the safe run: kept
        s.Save();
        var back = Settings.Load();
        Assert.True(back.Karaoke);
        Assert.Equal(40, back.Roundness);
        Assert.Equal("Lviv", back.City);
    }

    [Fact]
    public void APlainGeminiKeyIsEncryptedOnLoad()
    {
        const string key = "AIzaPlainTextKey1234567890";
        File.WriteAllText(Settings.FilePath, $$"""{ "City": "Rivne", "GeminiVoiceApiKey": "{{key}}" }""");
        var s = Settings.Load();
        Assert.Equal(key, s.GeminiVoiceApiKey);
        var file = File.ReadAllText(Settings.FilePath);
        Assert.DoesNotContain(key, file);
        Assert.DoesNotContain("\"GeminiVoiceApiKey\"", file);
        Assert.Contains("GeminiVoiceApiKeySealed", file);
        Assert.Equal("Rivne", Settings.Load().City);
        Assert.Equal(key, Settings.Load().GeminiVoiceApiKey);
    }

    [Fact]
    public void TheKeyNeverReachesTheLog()
    {
        const string key = "AIzaLogSecret9876543210";
        var s = new Settings { GeminiVoiceApiKey = key };
        Assert.NotEqual("", s.GeminiVoiceApiKeySealed);
        Assert.DoesNotContain(key, s.GeminiVoiceApiKeySealed);
        Log.Write("test: a message with " + key + " inside");
        var text = File.ReadAllText(Log.FilePath);
        Assert.Contains("<key>", text);
        Assert.DoesNotContain(key, text);
    }

    [Fact]
    public void AKeySealedElsewhereIsDroppedNotThrown()
    {
        File.WriteAllText(Settings.FilePath, """{ "GeminiVoiceApiKeySealed": "bm90IGEgcmVhbCBzZWFsZWQga2V5" }""");
        Assert.Equal("", Settings.Load().GeminiVoiceApiKey);
    }
}

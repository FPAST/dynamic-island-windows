using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class YandexVkTests
{
    [Theory]
    [InlineData("ru.yandex.desktop.music", "Яндекс Музыка")]
    [InlineData("A025C540.Yandex.Music_vfvw9svesycw6!App", "Яндекс Музыка")]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("308046B0AF4A39CB", "Браузер")]
    public void PlayerNames(string aumid, string name) => Assert.Equal(name, MediaService.PrettyAppName(aumid));

    [Fact]
    public void YandexAppFindsItsOwnAudio()
    {
        Assert.True(AudioMatch.Score("ru.yandex.desktop.music", "Яндекс Музыка", null) > 0);
        Assert.True(AudioMatch.Score("ru.yandex.desktop.music", "YandexMusic", null) > 0);
    }

    [Theory]
    [InlineData("GitHubDesktop")]
    [InlineData("YouTube Music Desktop App")]
    [InlineData("Telegram")]
    [InlineData("browser")]   // Yandex Browser
    public void YandexAppIsNotAnotherProgram(string process) => Assert.Equal(0, AudioMatch.Score("ru.yandex.desktop.music", process, null));

    [Theory]
    [InlineData("Анна Асти", "ANNA ASTI", true)]
    [InlineData("Мияги", "MiyaGi & Endspiel", true)]
    [InlineData("Монеточка", "Monetochka", true)]
    [InlineData("Макс Корж", "Max Korzh", true)]
    [InlineData("Кино", "Kino", true)]
    [InlineData("Океан Ельзи", "Okean Elzy", false)]   // too far apart to guess
    [InlineData("Баста", "Kasta", false)]
    public void CyrillicAndLatinArtists(string a, string b, bool same) => Assert.Equal(same, LyricsService.SameArtist(a, b));

    [Fact]
    public void TitleOnlySearchTakesTheLatinSpelling()
    {
        // LRCLIB's answer for "По барам" (real, shortened): another artist first, the right one in Latin letters
        var items = new List<LyricsCandidate>
        {
            new(195, true, true, false, "KARAT"),
            new(238, true, true, false, "Тайпан"),
            new(238, true, true, false, "ANNA ASTI"),
        };
        Assert.Equal(2, LyricsService.PickBest(items, 238.4, "Анна Асти"));
    }
}

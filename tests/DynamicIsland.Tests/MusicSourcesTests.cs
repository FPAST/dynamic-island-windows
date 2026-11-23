using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class MusicSourcesTests
{
    [Theory]
    [InlineData("Spotify.exe", null, "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", null, "Spotify")]
    [InlineData("Chrome._crx_cinhimbnkkghhklpknlkffjgod", null, "YouTube Music")]
    [InlineData("308046B0AF4A39CB", "Time is Running Out - YouTube Music — Mozilla Firefox", "YouTube Music")]
    [InlineData("Chrome", "Time is Running Out - YouTube - Google Chrome", "YouTube")]
    [InlineData("MSEdge", "Кукушка — Яндекс Музыка", "Яндекс Музыка")]
    [InlineData("Yandex.Music_abc!App", null, "Яндекс Музыка")]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", null, "Apple Music")]
    public void TheServiceIsFoundFromThePlayerOrThePageTitle(string appId, string? title, string service) =>
        Assert.Equal(service, MusicSources.Find(appId, title)?.Name);

    [Theory]
    [InlineData("AIMP.exe", null)]
    [InlineData("Chrome", "Some news site - Google Chrome")]
    public void ALocalPlayerOrAnUnknownPageHasNoService(string appId, string? title) => Assert.Null(MusicSources.Find(appId, title));

    [Fact]
    public void TheSearchFindsArtistAndTitle()
    {
        Assert.Equal("https://music.youtube.com/search?q=Muse%20Time%20is%20Running%20Out",
            MusicSources.SearchFor(MusicSources.Named("YouTube Music")!, "Muse — Time is Running Out"));
        Assert.Equal("spotify:search:Muse%20Time%20is%20Running%20Out",
            MusicSources.SearchFor(MusicSources.Named("Spotify")!, "Muse — Time is Running Out"));
        Assert.Equal("https://music.yandex.ru/search?text=%D0%9A%D0%B8%D0%BD%D0%BE%20%D0%9A%D1%83%D0%BA%D1%83%D1%88%D0%BA%D0%B0",
            MusicSources.SearchFor(MusicSources.Named("Яндекс Музыка")!, "Кино — Кукушка"));
        Assert.Null(MusicSources.Named("nothing"));
    }
}

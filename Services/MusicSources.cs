using System.Diagnostics;

namespace DynamicIsland.Services;

/// <summary>
/// Where a liked track was playing, and how to find it there again. Windows tells which program plays, not which
/// track page: the track is opened as a search in the same service (Spotify in its app, the rest on their sites).
/// </summary>
public static class MusicSources
{
    /// <summary>A service and its search address ({0} = the escaped "Artist Title").</summary>
    public sealed record Source(string Name, string Search);

    private static readonly (string[] Hints, Source Source)[] Known =
    [
        (["spotify"], new("Spotify", "spotify:search:{0}")),
        (["cinhimbnkkghhklpknlkffjgod", "youtube music"], new("YouTube Music", "https://music.youtube.com/search?q={0}")),
        (["yandex.music", "yandexmusic", "яндекс музыка", "yandex music"], new("Яндекс Музыка", "https://music.yandex.ru/search?text={0}")),
        (["applemusic", "apple music"], new("Apple Music", "https://music.apple.com/search?term={0}")),
        (["deezer"], new("Deezer", "https://www.deezer.com/search/{0}")),
        (["soundcloud"], new("SoundCloud", "https://soundcloud.com/search?q={0}")),
        (["vk.com", "vk music", "vk музыка", "вконтакте"], new("VK Музыка", "https://vk.com/audio?q={0}")),
        (["youtube"], new("YouTube", "https://www.youtube.com/results?search_query={0}")),
    ];

    /// <summary>
    /// The service from the player's id (AUMID) or, for a browser, the title of its window with the track in it
    /// ("Time is Running Out - YouTube Music"). Null: a local player, or a site that isn't known.
    /// </summary>
    public static Source? Find(string appId, string? windowTitle = null)
    {
        foreach (var text in new[] { appId, windowTitle })
        {
            if (string.IsNullOrEmpty(text)) continue;
            var t = text.ToLowerInvariant();
            foreach (var (hints, source) in Known)
                if (hints.Any(t.Contains)) return source;
        }
        return null;
    }

    /// <summary>A service by its name (as kept with a liked track).</summary>
    public static Source? Named(string? name) => Known.Select(k => k.Source).FirstOrDefault(s => s.Name == name);

    /// <summary>The address that finds this track in that service.</summary>
    public static string SearchFor(Source source, string query) =>
        string.Format(source.Search, Uri.EscapeDataString(query.Replace(" — ", " ")));

    /// <summary>For a browser: the title of its window that shows the track (the page title names the site).</summary>
    public static string? BrowserTitleWith(string title)
    {
        if (title.Length == 0) return null;
        foreach (var name in new[] { "chrome", "msedge", "firefox", "browser", "opera", "brave", "vivaldi" })
        {
            var all = Process.GetProcessesByName(name);
            try
            {
                foreach (var p in all)
                {
                    string t;
                    try { t = p.MainWindowTitle; } catch (InvalidOperationException) { continue; }   // exited meanwhile
                    if (t.Contains(title, StringComparison.OrdinalIgnoreCase)) return t;
                }
            }
            finally { foreach (var p in all) p.Dispose(); }
        }
        return null;
    }

    /// <summary>Opens the search; false when it could not be opened.</summary>
    public static bool Open(Source source, string query)
    {
        var url = SearchFor(source, query);
        if (url.StartsWith("https://", StringComparison.Ordinal)) { SafeShell.OpenUrl(url); return true; }
        try { using var _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return true; }
        catch (Exception ex) { Log.Write($"liked: open in {source.Name}: {ex.GetType().Name}: {ex.Message}"); return false; }
    }
}

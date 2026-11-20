using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Services;
using DynamicIsland.Views;

namespace DynamicIsland;

/// <summary>
/// Song words on the large music widget. They come from the same search as the island's (one request per track,
/// kept on disk) and follow the same smooth clock. The 10 Hz follow-up runs only while such a widget is on screen,
/// the song plays and the words are timed; otherwise nothing ticks.
/// </summary>
public partial class MainWindow
{
    private readonly List<WeakReference<LyricsView>> _lyricsViews = [];
    private readonly DispatcherTimer _lyricsTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    private LyricsView NewMusicLyrics()
    {
        if (_lyricsViews.Count == 0) _lyricsTimer.Tick += (_, _) => Measure("widget lyrics", TickMusicLyrics);
        var v = new LyricsView { Clickable = () => !_widgetsEditing };
        v.LineClicked += SeekToLyric;
        _lyricsViews.Add(new WeakReference<LyricsView>(v));
        return v;
    }

    private IEnumerable<LyricsView> LiveLyricsViews()
    {
        _lyricsViews.RemoveAll(r => !r.TryGetTarget(out var v) || PresentationSource.FromVisual(v) == null && v.IsLoaded);
        foreach (var r in _lyricsViews) if (r.TryGetTarget(out var v) && v.IsVisible && !PausedHost(v)) yield return v;
    }

    /// <summary>What every lyrics block shows now: the words, or why there are none.</summary>
    private void TickMusicLyrics()
    {
        var m = _lastMedia;
        bool hasTrack = m is { Title.Length: > 0 };
        TimeSpan at = hasTrack ? LyricsPosition() : TimeSpan.Zero;
        foreach (var v in LiveLyricsViews())
        {
            if (!hasTrack) v.ShowMessage(L.T("Здесь появится текст песни"));
            else if (_lyricsLoaded != _lyricsKey) v.ShowMessage(L.T("Ищу текст…"));
            else if (_currentLyrics is { Lines.Count: > 0 } l) { v.Show(l); v.Follow(at, m!.Duration); }
            else v.ShowMessage(_currentLyrics?.Instrumental == true ? L.T("Инструментальная композиция") : L.T("Текст не найден"));
        }
    }

    /// <summary>Once a second: the 10 Hz follow-up runs only when there is something to follow.</summary>
    private void LyricsControl()
    {
        bool want = _lastMedia?.IsPlaying == true && _currentLyrics?.Synced == true && !_fullscreenHidden && LiveLyricsViews().Any();
        if (want && !_lyricsTimer.IsEnabled) _lyricsTimer.Start();
        else if (!want && _lyricsTimer.IsEnabled) _lyricsTimer.Stop();
    }

    /// <summary>The widget editor: shift the words earlier or later, ±0.5 s in steps of 50 ms.</summary>
    private void ShiftLyrics(int deltaMs)
    {
        _settings.LyricsOffsetMs = deltaMs == 0 ? 0 : Math.Clamp(_settings.LyricsOffsetMs + deltaMs, -500, 500);
        _settings.Save();
        _lyricIndex = int.MinValue;   // the island's line is looked up again at once
        UpdateLyricLine();
        TickMusicLyrics();
    }

    private string LyricsOffsetText() =>
        _settings.LyricsOffsetMs == 0 ? L.T("Без сдвига")
        : L.F("{0} с", (_settings.LyricsOffsetMs > 0 ? "+" : "−") + (Math.Abs(_settings.LyricsOffsetMs) / 1000.0).ToString("0.00", System.Globalization.CultureInfo.CurrentCulture));
}

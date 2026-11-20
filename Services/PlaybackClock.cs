namespace DynamicIsland.Services;

/// <summary>
/// The playing position between the media controls' reports. Windows reports the position rarely and in jumps
/// (some players every few seconds, browsers late); this clock runs on its own from the last known position and
/// only takes the reported one again when the track changes, playback pauses or resumes, the user seeks, or the
/// two differ by more than 300 ms. Times are given in Stopwatch ticks so tests can drive it.
/// </summary>
public sealed class PlaybackClock
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(300);

    private readonly double _frequency;
    private string _key = "";
    private bool _playing;
    private TimeSpan _base;
    private long _baseTicks;
    private long _holdUntil;   // after a seek: the player's old position is ignored for a moment

    public PlaybackClock(double ticksPerSecond = 0) => _frequency = ticksPerSecond > 0 ? ticksPerSecond : System.Diagnostics.Stopwatch.Frequency;

    /// <summary>How many times it took the reported position (for logs and tests).</summary>
    public int Resyncs { get; private set; }

    public TimeSpan At(long ticks) => _playing ? _base + TimeSpan.FromSeconds((ticks - _baseTicks) / _frequency) : _base;

    /// <summary>A report from the media controls: track (any id), position, whether it plays.</summary>
    public void Report(string track, TimeSpan reported, bool playing, long ticks)
    {
        var local = At(ticks);
        bool agrees = (reported - local).Duration() <= Tolerance;
        if (ticks < _holdUntil && track == _key)
        {
            if (agrees) _holdUntil = 0;   // the player caught up with the seek
            else return;                  // still the position from before the seek
        }
        if (track != _key || playing != _playing || !agrees)
        {
            _key = track; _playing = playing; _base = reported; _baseTicks = ticks;
            Resyncs++;
        }
    }

    /// <summary>
    /// The user moved the position: start from there at once. The player confirms it later, so for up to
    /// 2 s the reports that still say the old position are ignored.
    /// </summary>
    public void Seeked(TimeSpan to, long ticks) { _base = to; _baseTicks = ticks; _holdUntil = ticks + (long)(2 * _frequency); }

    public TimeSpan Now => At(System.Diagnostics.Stopwatch.GetTimestamp());

    /// <summary>
    /// How far ahead the words should run for this player, ms. Browsers (Chrome, Edge, Firefox, Yandex Browser,
    /// Opera, and web apps installed from them) report the position once, when playback starts or seeks, and that
    /// report comes about half a second late, so everything after it is late by as much (measured on YouTube Music
    /// in Chrome: the words came 0.5–1 s after the singing). Desktop players report on time.
    /// </summary>
    public static int SourceLeadMs(string appId)
    {
        var id = appId.ToLowerInvariant();
        bool browser = id.Contains("chrome") || id.Contains("msedge") || id.Contains("firefox") || id.Contains("yandex.browser")
                       || id.Contains("opera") || id.Contains("brave") || id.Contains("vivaldi") || id.StartsWith("browser")
                       || id.Length >= 12 && id.All(Uri.IsHexDigit);   // Firefox shows up as a bare hash
        return browser ? 500 : 0;
    }
}

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// TikTok no. 1 ("Windows came alive") on demo data, each scene on the beat of its voiceover (times from the
/// voiceover's words). Only the Music, Weather and Timer pages are shown (Home has the user's own apps, Chat
/// their signed-in chat).
/// "--tiktok &lt;folder&gt;": the island on a painted stage with a drawn cursor, as --promo films it.
/// "--tiktok-screen &lt;folder&gt;": the real screen with the real wallpaper and the real cursor, moved by the
/// script after a 15 s countdown; moving the mouse or Esc stops it at once. Every window is minimized first and
/// brought back after ("Minimize all"), so no open window (a call, a chat) gets into the film; the live
/// wallpaper plays on. The camera never enlarges the screen (1:1 at most). The camera is logged per frame and
/// tools/reframe-screen.py cuts it out of the screen video. tools/compose-tiktok.py adds voice and subtitles.
/// </summary>
public partial class MainWindow
{
    private static Dictionary<string, (string, string)> TikTokCaptions() => new()
    {
        ["hook"] = ("Чего не хватало Windows?", ""),
        ["intro"] = ("Атолл 2.0", "динамический остров для Windows"),
        ["springs"] = ("Живые пружины", "сначала ширина, потом высота"),
        ["press"] = ("Отзывается на нажатие", ""),
        ["music"] = ("Обложка-пластинка", "крутится, пока играет музыка"),
        ["wave"] = ("Волна в названии", "буквы тянутся к курсору"),
        ["jelly"] = ("Желейная перемотка", ""),
        ["tabs"] = ("Резиновые вкладки", ""),
        ["touch"] = ("Его хочется трогать", ""),
        ["end"] = ("Атолл 2.0", "уже скоро · подпишись"),
    };

    private sealed class TikTokStopped : Exception;

    // the real-screen run: where the script last put the cursor, the camera per frame, the stop
    private Native.POINT? _realSet;
    private readonly List<double[]> _camLog = [];
    private bool _stopFilm;
    private Rect _monitor;

    private async Task RunTikTokAsync(string folder, bool screen)
    {
        folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        _captions = TikTokCaptions();
        _promoLink = "";
        Island.IsHitTestVisible = false;
        _settings.ExpandOnHover = false;
        if (screen)
        {
            _settings.Glass = true;   // over the real wallpaper the frost is the point
            ApplyGlass();
            _fakeCursor = null;       // the island's eyes and reach follow the real cursor
            var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            _monitor = new Rect(b.X, b.Y, b.Width, b.Height);
        }
        RenderPromoStills(folder);
        await Task.Delay(6000);   // weather + layout
        UpdateMediaUi();
        SetMode(Mode.Compact);

        Window? stage = null;
        if (screen)
        {
            Shell("MinimizeAll");
            await Task.Delay(800);
            Native.KeepTopmost(_hwnd);
            for (int i = 15; i > 0; i--)
            {
                ShowToast("", $"Запись через {i}", "Отпустите мышь. Шевельнёте мышью или Esc — стоп", null, null, 1300);
                await Task.Delay(1000);
            }
            _pinnedOpen = false; SetMode(Mode.Compact);
            await Task.Delay(700);
        }
        else
        {
            stage = BuildStageWindow();
            stage.Show();
        }
        CompositionTarget.Rendering += screen ? ScreenFilmFrame : PromoFrame;
        _camTarget = FollowIsland;
        (_camC, _camZ) = FollowIsland();
        _cur = _curTarget = screen ? Park() : Center(Island, 0.8, 1) + new Vector(60, 220);
        await Task.Delay(1000);

        var path = Path.Combine(folder, screen ? "screen.mp4" : "stage.mp4");
        var (rec, finished) = screen ? await StartScreenRecordingAsync(path) : await StartStageRecordingAsync(stage!, path);   // the clock starts at the first frame
        try
        {
            await FilmTikTok1(screen);
        }
        catch (TikTokStopped)
        {
            Log.Write("tiktok: stopped by the mouse or Esc");
            ShowToast("", "Запись остановлена", "Мышь или Esc", null, null, 3000);
        }
        double total = _promoClock.Elapsed.TotalSeconds;
        rec.Stop();
        var file = await finished;
        CompositionTarget.Rendering -= screen ? ScreenFilmFrame : PromoFrame;
        File.WriteAllText(Path.Combine(folder, "captions.json"), JsonSerializer.Serialize(new
        {
            total,
            stopped = _stopFilm,
            captions = _captionLog.Select(c => new { key = c.Key, t = c.T }),
        }));
        if (screen) File.WriteAllText(Path.Combine(folder, "camera.json"), JsonSerializer.Serialize(new { monitor = new[] { _monitor.Width, _monitor.Height }, frames = _camLog }));
        Log.Write($"tiktok: {file ?? "failed"}, {total:0.00} s");
        if (screen) Shell("UndoMinimizeALL");
        await Task.Delay(_stopFilm ? 3000 : 500);
        Application.Current.Shutdown();
    }

    /// <summary>Explorer's "Minimize all" / its undo, as the taskbar does them (not keystrokes).</summary>
    private static void Shell(string verb)
    {
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            var shell = t == null ? null : Activator.CreateInstance(t);
            t?.InvokeMember(verb, System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
        }
        catch (Exception e) { Log.Write("tiktok: " + verb + ": " + e.Message); }
    }

    /// <summary>Where the cursor waits while it is not in the scene: low on the right of the screen, out of the camera.</summary>
    private Point Park() => Root.PointFromScreen(new Point(_monitor.X + _monitor.Width * 0.88, _monitor.Y + _monitor.Height * 0.9));

    private async Task<(ScreenRecorderLib.Recorder Rec, Task<string?> Finished)> StartScreenRecordingAsync(string path)
    {
        var options = new ScreenRecorderLib.RecorderOptions
        {
            SourceOptions = new ScreenRecorderLib.SourceOptions
            {
                RecordingSources = { new ScreenRecorderLib.DisplayRecordingSource(ScreenRecorderLib.DisplayRecordingSource.MainMonitor) { IsCursorCaptureEnabled = true } },
            },
            AudioOptions = new ScreenRecorderLib.AudioOptions { IsAudioEnabled = false },
            OutputOptions = new ScreenRecorderLib.OutputOptions { RecorderMode = ScreenRecorderLib.RecorderMode.Video },
            VideoEncoderOptions = new ScreenRecorderLib.VideoEncoderOptions
            {
                Framerate = 60, IsFixedFramerate = true, IsHardwareEncodingEnabled = true, Bitrate = 60_000_000,
                Encoder = new ScreenRecorderLib.H264VideoEncoder { BitrateMode = ScreenRecorderLib.H264BitrateControlMode.CBR, EncoderProfile = ScreenRecorderLib.H264Profile.High },
            },
            MouseOptions = new ScreenRecorderLib.MouseOptions { IsMousePointerEnabled = true },
        };
        var rec = ScreenRecorderLib.Recorder.CreateRecorder(options);
        var started = new TaskCompletionSource();
        var finished = new TaskCompletionSource<string?>();
        rec.OnStatusChanged += (_, e) =>
        {
            if (e.Status != ScreenRecorderLib.RecorderStatus.Recording) return;
            _promoClock.Restart();
            started.TrySetResult();
        };
        rec.OnRecordingComplete += (_, e) => finished.TrySetResult(e.FilePath);
        rec.OnRecordingFailed += (_, e) => { Log.Write("screen recording failed: " + e.Error); started.TrySetResult(); finished.TrySetResult(null); };
        if (File.Exists(path)) File.Delete(path);
        rec.Record(path);
        await started.Task;
        return (rec, finished.Task);
    }

    /// <summary>
    /// The real-screen frame: the camera eases like the stage's (zoom capped so the screen video is not blown up
    /// too far), its view in monitor pixels is logged; the real cursor is moved along; the user taking the mouse
    /// back (it is not where the script put it) or Esc stops the film.
    /// </summary>
    private void ScreenFilmFrame(object? sender, EventArgs e)
    {
        double t = _promoClock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(t - _lastFrameT, 0, 0.1);
        _lastFrameT = t;
        double scale = VisualTreeHelper.GetDpi(Root).DpiScaleX;

        var (tc, tz) = _camTarget();
        tz = Math.Min(tz, scale);
        double k = Math.Min(1, dt * 4.5);
        _camC = new Point(_camC.X + (tc.X - _camC.X) * k, _camC.Y + (tc.Y - _camC.Y) * k);
        _camZ = Math.Exp(Math.Log(_camZ) + (Math.Log(tz) - Math.Log(_camZ)) * k);
        try
        {
            var c = Root.PointToScreen(_camC);
            double vw = PromoStage.Width / _camZ * scale, vh = PromoStage.Height / _camZ * scale;
            _camLog.Add([Math.Round(t, 4), Math.Round(c.X - _monitor.X - vw / 2, 1), Math.Round(c.Y - _monitor.Y - vh * 0.42, 1), Math.Round(vw, 1), Math.Round(vh, 1)]);
        }
        catch (InvalidOperationException) { /* not on screen */ }

        if (_stopFilm) return;
        if (Native.KeyDown(0x1B)) { _stopFilm = true; Log.Write($"tiktok: Esc at {t:0.00} s"); return; }
        Native.GetCursorPos(out var now);
        if (_realSet is Native.POINT set && Math.Abs(now.X - set.X) + Math.Abs(now.Y - set.Y) > 40)
        {
            _stopFilm = true;
            Log.Write($"tiktok: the cursor is at {now.X},{now.Y}, the script put it at {set.X},{set.Y} ({t:0.00} s)");
            return;
        }

        var target = _cursorOn ? _curTarget : Park();
        double kc = Math.Min(1, dt * 6);
        _cur = new Point(_cur.X + (target.X - _cur.X) * kc, _cur.Y + (target.Y - _cur.Y) * kc);
        try
        {
            var s = Root.PointToScreen(_cur);
            // inside the monitor: Windows holds the cursor at its edge, and that must not look like the user's hand
            var p = new Native.POINT
            {
                X = (int)Math.Round(Math.Clamp(s.X, _monitor.Left + 2, _monitor.Right - 3)),
                Y = (int)Math.Round(Math.Clamp(s.Y, _monitor.Top + 2, _monitor.Bottom - 3)),
            };
            Native.SetCursorPos(p.X, p.Y);
            _realSet = p;
        }
        catch (InvalidOperationException) { /* not on screen */ }
    }

    private async Task FilmTikTok1(bool screen)
    {
        async Task At(double t)
        {
            while (_promoClock.Elapsed.TotalSeconds < t)
            {
                if (_stopFilm) throw new TikTokStopped();
                await Task.Delay(Math.Clamp((int)((t - _promoClock.Elapsed.TotalSeconds) * 1000), 1, 30));
            }
            if (_stopFilm) throw new TikTokStopped();
        }
        void Open(System.Windows.Controls.RadioButton tab) { _pinnedOpen = true; tab.IsChecked = true; Expand(); }
        void Fold() { _pinnedOpen = false; SetMode(Mode.Compact); }
        async Task Press()
        {
            if (!screen) Click();
            Squish(1 - 0.045 * LiquidK);
            _press.Target = 96; StartRendering();
            await Task.Delay(170);
            _press.Target = 100; Unsquish(1.6); StartRendering();
        }
        void Playing(bool on)
        {
            _demoMedia = _demoMedia! with { Position = _demoMedia.CurrentPosition, PositionUpdatedAt = DateTimeOffset.Now, IsPlaying = on };
            UpdateMediaUi();
        }
        (Point, double) On(FrameworkElement e, double z, double dy = 0) => (Center(e) + new Vector(0, dy), z);
        void Pointer(FrameworkElement e, double fx, double fy) { _curTarget = Center(e, fx, fy); _cursorOn = true; }

        // "Знаете, чего всегда не хватало Windows?" — the folded pill
        Caption("hook");
        // "Вот этого!" — it opens
        await At(2.8);
        Open(TabMusic);
        // "Это Атолл, динамический остров, как на айфоне."
        await At(4.1);
        Caption("intro");
        // "Только у вас на компьютере."
        await At(7.3);
        Fold();
        // "В версии 2.0 он научился двигаться по-настоящему. Смотрите, сначала тянется в ширину, а потом мягко опускается."
        await At(8.9);
        Caption("springs");
        await At(12.9);
        Open(TabMusic);
        await At(15.9);
        Fold();
        // "Нажимаете, он чуть пружинит под пальцем. Как живой!"
        await At(16.6);
        Caption("press");
        Pointer(Island, 0.5, 0.5);
        await At(17.9); await Press();
        await At(18.9); await Press();
        await At(19.6);
        _cursorOn = false;
        // "А теперь музыка! Обложка крутится как виниловая пластинка, а рядом прыгают полоски эквалайзера."
        await At(20.5);
        Caption("music");
        Open(TabMusic);
        await At(21.8);
        _camTarget = () => On(ArtRecord, 3.4, 30);
        await At(24.4);
        _camTarget = () => On(MusicBars, 3.0, -10);
        // "Ставите паузу, пластинка замирает. Включаете, снова крутится."
        await At(26.6);
        _camTarget = () => On(ArtRecord, 3.4, 30);
        await At(27.3); Playing(false);
        await At(29.6); Playing(true);
        // "По названию трека бежит волна. Буквы по очереди становятся жирными."
        await At(31.3);
        Caption("wave");
        _camTarget = () => On(TrackTitle, 3.6, 20);
        // "Наведите мышку, и они тянутся к ней."
        await At(35.0);
        Pointer(TrackTitle, 0.1, 1.4);
        var title = (Views.WeightWaveText)TrackTitle;
        for (double t = 35.8; t < 37.9; t += 0.04)
        {
            await At(t);
            double f = (t - 35.8) / 2.1, x = f < .5 ? 0.15 + 1.5 * f : 1.15 - 0.8 * (f - .5) * 2;
            Pointer(TrackTitle, x, 0.6);
            title.TestPointer(TrackTitle.ActualWidth * x);
        }
        title.TestPointer(null);
        _cursorOn = false;
        // "Перемотка желейная! Тянете бегунок, и он растягивается как мармелад."
        await At(38.0);
        Caption("jelly");
        var seek = (Views.JellySlider)SeekSlider;
        _camTarget = () => On(SeekSlider, 2.4, -20);
        double sw = seek.ActualWidth;
        await At(39.3);
        Pointer(SeekSlider, (10 + 0.3 * (sw - 20)) / sw, 0.5);
        await At(40.0);
        seek.TestPress(10 + 0.3 * (sw - 20));
        for (double t = 40.4; t < 42.4; t += 0.03)
        {
            await At(t);
            double f = (t - 40.4) / 2.0, e = f < .5 ? 2 * f * f : 1 - Math.Pow(-2 * f + 2, 2) / 2;
            double x = 10 + (0.3 + 0.5 * e) * (sw - 20);
            seek.TestDrag(x);
            Pointer(SeekSlider, x / sw, 0.5);
        }
        await At(42.6);
        seek.TestRelease();
        // "И вкладки островка. Подсветка не прыгает, а переезжает к нужной, растягиваясь по пути как резинка."
        await At(42.9);
        Caption("tabs");
        _camTarget = () => On(TabStrip, 2.3, 40);
        Pointer(TabWeather, 0.5, 0.6);
        await At(44.4); TabWeather.IsChecked = true;
        Pointer(TabTimer, 0.5, 0.6);
        await At(45.7); TabTimer.IsChecked = true;
        Pointer(TabMusic, 0.5, 0.6);
        await At(47.1); TabMusic.IsChecked = true;
        // "Мы хотели, чтобы островок не просто работал. А чтобы его хотелось трогать."
        await At(48.9);
        Caption("touch");
        _cursorOn = false;
        _camTarget = FollowIsland;
        Fold();
        await At(51.5);
        Pointer(Island, 0.5, 0.5);
        await At(52.3); await Press();
        Open(TabMusic);
        // "Атолл 2.0 уже скоро! Подпишитесь, чтобы не пропустить!"
        await At(53.3);
        _cursorOn = false;
        Caption("end");
        Fold();
        await At(57.6);
    }
}

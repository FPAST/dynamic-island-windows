using DynamicIsland.Views;
using Xunit;

namespace DynamicIsland.Tests;

/// <summary>The real karaoke view, rendered: half the line sung must light the left half only, in any script.</summary>
public class KaraokeViewRenderTests
{
    [Theory]
    [InlineData("알랑가 몰라, we like, we-we-we like party, hey")]
    [InlineData("我们一起走过的那些日子 — and then")]
    [InlineData("君の名前を呼んでいた - kimi no na")]
    [InlineData("Я иду — и вдруг")]
    public void HalfSungLightsOnlyTheLeftPart(string text)
    {
        Exception? error = null;
        var t = new Thread(() => { try { Check(text); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
    }

    private static void Check(string text)
    {
        var view = new KaraokeView { MaxLineWidth = 2000 };
        view.Resources["Fg"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
        view.Resources["Accent"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
        view.SetLine(text, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        Thread.Sleep(1000);                                   // the line's entrance spring has settled
        double length = KaraokeTiming.Duration(text, 10);
        view.Sync(TimeSpan.FromSeconds(length / 2), playing: false);
        view.Measure(new System.Windows.Size(2000, 20));
        view.Arrange(new System.Windows.Rect(view.DesiredSize));
        int w = (int)Math.Ceiling(view.ActualWidth), h = 20;
        var host = new System.Windows.Controls.Border { Background = System.Windows.Media.Brushes.Black, Child = view };
        host.Measure(new System.Windows.Size(w, h)); host.Arrange(new System.Windows.Rect(0, 0, w, h));
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(host);
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);
        int Col(int x) { int m = 0; for (int y = 0; y < h; y++) m = Math.Max(m, (int)px[(y * w + x) * 4 + 2]); return m; }
        // a dim glyph (6+ columns of dim ink in a row) with bright ink further right = the light is split
        int run = 0, dimAt = -1, brightAfter = -1, anyBright = -1;
        for (int x = 0; x < w; x++)
        {
            int c = Col(x);
            if (c > 200) { anyBright = x; if (dimAt >= 0 && brightAfter < 0) brightAfter = x; }
            run = c is > 60 and < 150 ? run + 1 : 0;
            if (run >= 6 && dimAt < 0) dimAt = x - 5;
        }
        Assert.True(anyBright >= 0, "nothing lit at half the line");
        Assert.True(dimAt >= 0, "nothing dim at half the line");
        Assert.True(brightAfter < 0, $"dim ink from x={dimAt}, then bright again at x={brightAfter}: the light is split");
    }
}

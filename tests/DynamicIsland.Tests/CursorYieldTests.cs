using System.Windows;
using DynamicIsland.Services;
using Xunit;

namespace DynamicIsland.Tests;

public class CursorYieldTests
{
    // the island: x 800…1100, y 0…40 (screen pixels); a frame every 33 ms
    private static bool Inside(Point p) => p.X is >= 800 and <= 1100 && p.Y is >= 0 and <= 40;

    private static (int Result, long Ms) Move(CursorYield y, long ms, Point from, Point to, int frames, bool under = true, bool game = false)
    {
        int last = 0;
        for (int i = 1; i <= frames; i++)
        {
            var p = from + (to - from) * ((double)i / frames);
            ms += 33;
            int r = y.Step(ms, p, Inside(p), p.Y <= 0, under, game);
            if (r != 0) last = r;
        }
        return (last, ms);
    }

    [Fact]
    public void A_cursor_flying_in_makes_it_step_aside()
    {
        var y = new CursorYield();
        var (r, _) = Move(y, 0, new Point(950, 500), new Point(950, 20), 4);   // ~480 px in 130 ms
        Assert.Equal(1, r);
        Assert.True(y.Yielding);
    }

    [Fact]
    public void A_cursor_coming_slowly_does_not()
    {
        var y = new CursorYield();
        Move(y, 0, new Point(950, 300), new Point(950, 20), 40);   // 280 px in 1.3 s
        Assert.False(y.Yielding);
    }

    [Fact]
    public void Over_the_desktop_it_never_steps_aside()
    {
        var y = new CursorYield();
        Move(y, 0, new Point(950, 500), new Point(950, 20), 4, under: false);
        Assert.False(y.Yielding);
    }

    [Fact]
    public void In_a_game_it_steps_aside_even_for_a_slow_cursor_and_stays_away()
    {
        var y = new CursorYield();
        var (r, ms) = Move(y, 0, new Point(950, 300), new Point(950, 20), 40, game: true);
        Assert.Equal(1, r);
        // resting on it for a second, even at the top edge, does not bring it back in a game
        Move(y, ms, new Point(950, 0), new Point(950, 0), 30, game: true);
        Assert.True(y.Yielding);
    }

    [Fact]
    public void Resting_on_it_brings_it_back()
    {
        var y = new CursorYield();
        var (_, ms) = Move(y, 0, new Point(950, 500), new Point(950, 20), 4);
        var (r, ms2) = Move(y, ms, new Point(950, 20), new Point(950, 20), 6);   // 200 ms: not yet
        Assert.True(y.Yielding);
        (r, _) = Move(y, ms2, new Point(950, 20), new Point(951, 21), 6);       // 400 ms, a pixel of jitter
        Assert.Equal(-1, r);
        Assert.False(y.Yielding);
    }

    [Fact]
    public void The_top_edge_brings_it_back_at_once()
    {
        var y = new CursorYield();
        var (_, ms) = Move(y, 0, new Point(950, 500), new Point(950, 20), 4);
        var (r, _) = Move(y, ms, new Point(950, 20), new Point(950, 0), 1);
        Assert.Equal(-1, r);
    }

    [Fact]
    public void Leaving_brings_it_back_and_it_does_not_step_aside_again_until_the_cursor_leaves()
    {
        var y = new CursorYield();
        var (_, ms) = Move(y, 0, new Point(950, 500), new Point(950, 20), 4);
        var (r, ms2) = Move(y, ms, new Point(950, 20), new Point(950, 20), 12);   // back by resting
        Assert.Equal(-1, r);
        // moving about on it quickly now: it stays (the user meant it)
        (r, ms2) = Move(y, ms2, new Point(950, 20), new Point(1080, 25), 2);
        Assert.False(y.Yielding);
        // out and flying in again: steps aside again
        (_, ms2) = Move(y, ms2, new Point(1080, 25), new Point(1080, 400), 3);
        (r, _) = Move(y, ms2, new Point(1080, 400), new Point(1000, 10), 3);
        Assert.Equal(1, r);
    }
}
